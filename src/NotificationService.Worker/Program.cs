using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Persistence;
using Serilog;

// Delivers notifications. It exposes only health endpoints, so Compose and orchestrators can tell whether it is up.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Notifications");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Notifications is not configured.");
}

builder.Services.AddNotificationsPersistence(connectionString);
builder.Services.AddNotificationsMessaging(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<TopologySetup>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationsDbContext>("database", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
