using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Delivery;
using NotificationService.IntegrationTests.Support;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Boots the real API against throwaway PostgreSQL, RabbitMQ and Mailpit containers shared by the whole
/// collection. The worker's relay and consumer run in the same process, so a request goes all the way to a
/// delivered email inside one test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminKey = "ns_testadm1_admin-key-used-only-by-the-integration-tests";
    private const ushort SmtpPort = 1025;
    private const ushort MailpitApiPort = 8025;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management").Build();
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.30.2")
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(MailpitApiPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(MailpitApiPort).ForPath("/livez")))
        .Build();

    public string DatabaseConnectionString => _database.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public Uri MailpitUri => new($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(MailpitApiPort)}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Notifications", DatabaseConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", RabbitMqConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Bootstrap:AdminApiKey", AdminKey);
        builder.UseSetting("Bootstrap:SampleTemplates", "true");
        builder.UseSetting("Smtp:Host", _mailpit.Hostname);
        builder.UseSetting("Smtp:Port", _mailpit.GetMappedPublicPort(SmtpPort).ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Short waits so failure paths finish within a test.
        builder.UseSetting("Delivery:RetryDelays:0", "00:00:01");
        builder.UseSetting("Delivery:RetryDelays:1", "00:00:01");
        builder.UseSetting("Delivery:RelayInterval", "00:00:00.100");
        builder.UseSetting("Delivery:SendingLease", "00:00:05");
        builder.ConfigureServices((context, services) =>
        {
            services.AddNotificationsWorker(context.Configuration);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<SmtpEmailSender>();
            services.AddSingleton<IEmailSender, TestEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _rabbitMq.StartAsync(), _mailpit.StartAsync());
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_database.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask(), _mailpit.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
