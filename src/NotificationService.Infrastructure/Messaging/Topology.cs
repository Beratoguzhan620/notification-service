using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>
/// Declares the queues. Retries need no scheduler: a failed message is published to a retry queue whose
/// messages expire after the delay and are dead-lettered back to the delivery queue.
/// <code>
/// deliver ──fail──▶ retry.5000ms ──TTL──▶ deliver ──fail──▶ retry.30000ms ──TTL──▶ deliver ──fail──▶ dead
/// </code>
/// </summary>
public sealed class Topology(IOptions<DeliveryOptions> options)
{
    public IReadOnlyList<string> RetryQueues =>
        options.Value.EffectiveRetryDelays.Select(Queues.Retry).Distinct(StringComparer.Ordinal).ToList();

    public async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // Quorum queues are replicated and durable. Rejected or over-delivered messages go to the dead queue.
        await channel.QueueDeclareAsync(Queues.Deliver, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = Queues.Dead,
                // A message that keeps crashing the consumer before it can be acknowledged stops after this many tries.
                ["x-delivery-limit"] = 10,
            },
            cancellationToken: cancellationToken);

        foreach (var delay in options.Value.EffectiveRetryDelays.DistinctBy(Queues.Retry))
        {
            await channel.QueueDeclareAsync(Queues.Retry(delay), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-message-ttl"] = (long)delay.TotalMilliseconds,
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = Queues.Deliver,
                    // Expired messages move on only after the delivery queue has confirmed them.
                    ["x-dead-letter-strategy"] = "at-least-once",
                    ["x-overflow"] = "reject-publish",
                },
                cancellationToken: cancellationToken);
        }

        await channel.QueueDeclareAsync(Queues.Dead, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = (long)options.Value.DeadLetterRetention.TotalMilliseconds,
            },
            cancellationToken: cancellationToken);
    }
}
