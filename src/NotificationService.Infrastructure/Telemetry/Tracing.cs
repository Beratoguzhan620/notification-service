using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NotificationService.Infrastructure.Telemetry;

/// <summary>
/// One trace per notification: the HTTP request, the outbox relay publishing it, the consumer handling it and
/// the email being sent. The trace context crosses RabbitMQ in the W3C <c>traceparent</c> header.
/// </summary>
public static class Tracing
{
    public const string SourceName = "NotificationService.Messaging";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Exports to an OTLP endpoint (Jaeger in Compose) when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.</summary>
    public static IServiceCollection AddNotificationsTracing(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(SourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/scalar"))
                .AddNpgsql()
                .AddOtlpExporter());
        return services;
    }

    /// <summary>Starts a span whose parent is a stored or received <c>traceparent</c>, if there is one.</summary>
    public static Activity? StartLinked(string name, ActivityKind kind, string? traceParent)
    {
        var parent = ActivityContext.TryParse(traceParent, null, isRemote: true, out var context) ? context : default;
        return Source.StartActivity(name, kind, parent);
    }
}
