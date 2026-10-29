using System.Text;
using Microsoft.Extensions.Options;
using NotificationService.Infrastructure.Messaging;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace NotificationService.IntegrationTests;

/// <summary>A broker of its own, so nothing else consumes the queues these tests look into.</summary>
public sealed class BrokerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.2-management").Build();

    public string ConnectionString => _rabbitMq.GetConnectionString();

    public Task InitializeAsync() => _rabbitMq.StartAsync();

    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();
}

/// <summary>The queue layout itself: retries come back after their delay and rejected messages end up dead.</summary>
public sealed class TopologyTests(BrokerFixture broker) : IClassFixture<BrokerFixture>
{
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_message_in_a_retry_queue_returns_to_delivery_after_its_delay()
    {
        await using var connection = new RabbitMqConnection(broker.ConnectionString);
        await using var channel = await DeclareAsync(connection);
        await channel.QueuePurgeAsync(Queues.Deliver);
        var id = Guid.NewGuid().ToString("N");

        await channel.BasicPublishAsync(string.Empty, Queues.Retry(ShortDelay), mandatory: true,
            new BasicProperties { MessageId = id, Persistent = true }, Encoding.UTF8.GetBytes("{}"));
        var immediately = await channel.BasicGetAsync(Queues.Deliver, autoAck: true);
        var later = await WaitForAsync(channel, Queues.Deliver, id);

        Assert.Null(immediately);
        Assert.NotNull(later);
    }

    [Fact]
    public async Task A_rejected_message_goes_to_the_dead_queue()
    {
        await using var connection = new RabbitMqConnection(broker.ConnectionString);
        await using var channel = await DeclareAsync(connection);
        await channel.QueuePurgeAsync(Queues.Deliver);
        var id = Guid.NewGuid().ToString("N");

        await channel.BasicPublishAsync(string.Empty, Queues.Deliver, mandatory: true,
            new BasicProperties { MessageId = id, Persistent = true }, Encoding.UTF8.GetBytes("not json"));
        var delivered = await WaitForAsync(channel, Queues.Deliver, id, ack: false);
        await channel.BasicRejectAsync(delivered!.DeliveryTag, requeue: false);
        var dead = await WaitForAsync(channel, Queues.Dead, id);

        Assert.NotNull(dead);
    }

    [Fact]
    public async Task Declaring_twice_is_harmless()
    {
        await using var connection = new RabbitMqConnection(broker.ConnectionString);
        await using var first = await DeclareAsync(connection);
        await using var second = await DeclareAsync(connection);

        var deliver = await second.QueueDeclarePassiveAsync(Queues.Deliver);
        var retry = await second.QueueDeclarePassiveAsync(Queues.Retry(ShortDelay));

        Assert.Equal(Queues.Deliver, deliver.QueueName);
        Assert.Equal(Queues.Retry(ShortDelay), retry.QueueName);
    }

    private static async Task<IChannel> DeclareAsync(RabbitMqConnection connection)
    {
        var topology = new Topology(Options.Create(new DeliveryOptions { RetryDelays = [ShortDelay, ShortDelay] }));
        var open = await connection.GetAsync(CancellationToken.None);
        var channel = await open.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await topology.DeclareAsync(channel, CancellationToken.None);
        return channel;
    }

    /// <summary>Polls a queue for one message, skipping others left by earlier tests.</summary>
    private static async Task<BasicGetResult?> WaitForAsync(IChannel channel, string queue, string messageId, bool ack = true)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: false);
            if (result is null)
            {
                await Task.Delay(100);
                continue;
            }

            if (result.BasicProperties.MessageId != messageId)
            {
                await channel.BasicAckAsync(result.DeliveryTag, multiple: false);
                continue;
            }

            if (ack)
            {
                await channel.BasicAckAsync(result.DeliveryTag, multiple: false);
            }

            return result;
        }

        return null;
    }
}
