namespace NotificationService.Infrastructure.Delivery;

/// <summary>
/// A message the consumer has finished with. RabbitMQ delivers at least once; a message id found here is
/// acknowledged without doing the work again, even when the notification itself was replayed since.
/// </summary>
public sealed class ProcessedMessage
{
    public Guid MessageId { get; init; }

    public Guid NotificationId { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
