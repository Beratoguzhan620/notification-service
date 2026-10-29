using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace NotificationService.IntegrationTests;

/// <summary>
/// Boots the real API against throwaway PostgreSQL and RabbitMQ containers shared by the whole collection.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminKey = "ns_testadm1_admin-key-used-only-by-the-integration-tests";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management").Build();

    public string DatabaseConnectionString => _database.GetConnectionString();

    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Notifications", DatabaseConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", RabbitMqConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Bootstrap:AdminApiKey", AdminKey);
        builder.UseSetting("Bootstrap:SampleTemplates", "true");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _rabbitMq.StartAsync());
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_database.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
