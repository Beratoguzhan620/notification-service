using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Infrastructure.Delivery;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Outbox;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The database, used by both the API and the workers.</summary>
    public static IServiceCollection AddNotificationsPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NotificationsDbContext>(options => ConfigureDbContext(options, connectionString));
        return services;
    }

    /// <summary>The RabbitMQ connection and queue layout, used by the workers.</summary>
    public static IServiceCollection AddNotificationsMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("RabbitMq");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:RabbitMq is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__RabbitMq environment variable.");
        }

        services.Configure<DeliveryOptions>(configuration.GetSection(DeliveryOptions.Section));
        services.AddSingleton(new RabbitMqConnection(connectionString));
        services.AddSingleton<Topology>();
        return services;
    }

    /// <summary>
    /// Everything a worker runs: queue setup, the outbox relay and the delivery consumer. The integration tests
    /// add the same services to the API host, so a whole delivery happens inside one test process.
    /// </summary>
    public static IServiceCollection AddNotificationsWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddNotificationsMessaging(configuration);
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.AddSingleton<Publisher>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<INotificationChannel, EmailChannel>();
        services.AddSingleton<INotificationChannel, InAppChannel>();
        services.AddScoped<NotificationProcessor>();
        services.AddHostedService<TopologySetup>();
        services.AddHostedService<OutboxRelay>();
        services.AddHostedService<DeliveryConsumer>();
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
