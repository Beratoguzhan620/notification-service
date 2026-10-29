using RabbitMQ.Client;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>
/// A channel with publisher confirms: <see cref="PublishAsync"/> returns only once RabbitMQ has taken
/// responsibility for the message, and throws if it refused it or could not route it.
/// </summary>
public sealed class Publisher(RabbitMqConnection connection) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(string queue, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                if (_channel is not null)
                {
                    await _channel.DisposeAsync();
                }

                var open = await connection.GetAsync(cancellationToken);
                _channel = await open.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    cancellationToken);
            }

            await _channel.BasicPublishAsync(string.Empty, queue, mandatory: true, properties, body, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }
}
