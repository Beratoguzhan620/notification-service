using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Api.Features.Auth;
using NotificationService.Api.Features.Clients;
using NotificationService.Api.Features.DeadLetters;
using NotificationService.Api.Features.Inbox;
using NotificationService.Api.Features.Notifications;
using NotificationService.Api.Features.Preferences;
using NotificationService.Api.Features.Templates;
using NotificationService.Api.Logging;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Telemetry;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Notifications");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Notifications is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Notifications environment variable.");
}

// The API writes to the database only; the workers talk to RabbitMQ.
builder.Services.AddNotificationsPersistence(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNotificationsTracing(builder.Configuration, "notification-api");
builder.Services.AddScoped<NotificationRequests>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApiKeyAuthentication();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationsDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Notification Service")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapClientEndpoints();
app.MapTemplateEndpoints();
app.MapPreferenceEndpoints();
app.MapNotificationEndpoints();
app.MapInboxEndpoints();
app.MapDeadLetterEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

await FirstAdminSeeder.SeedAsync(app.Services);
await SampleTemplates.SeedAsync(app.Services);

await app.RunAsync();

public partial class Program;
