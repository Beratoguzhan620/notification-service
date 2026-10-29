using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NotificationService.Infrastructure.Messaging;

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var open = await connection.GetAsync(cancellationToken);
            return open.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The connection is closed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", ex);
        }
    }
}
