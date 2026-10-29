using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>Declares the queues when a worker starts, waiting for RabbitMQ if it is still coming up.</summary>
public sealed partial class TopologySetup(RabbitMqConnection connection, Topology topology, ILogger<TopologySetup> logger) : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var open = await connection.GetAsync(cancellationToken);
                await using var channel = await open.CreateChannelAsync(cancellationToken: cancellationToken);
                await topology.DeclareAsync(channel, cancellationToken);
                LogDeclared(logger);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 30)
            {
                LogWaiting(logger, attempt, ex.Message);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ queues are declared")]
    private static partial void LogDeclared(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waiting for RabbitMQ (attempt {Attempt}): {Reason}")]
    private static partial void LogWaiting(ILogger logger, int attempt, string reason);
}
