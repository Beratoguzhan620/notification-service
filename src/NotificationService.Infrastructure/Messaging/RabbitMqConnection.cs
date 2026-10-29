using RabbitMQ.Client;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>
/// One connection per process, opened on first use. The client library recovers it and the declared
/// topology after a network failure; channels are created per publisher or consumer.
/// </summary>
public sealed class RabbitMqConnection(string connectionString) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } existing)
            {
                return existing;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString),
                ClientProvidedName = $"notification-service:{Environment.MachineName}:{Environment.ProcessId}",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
