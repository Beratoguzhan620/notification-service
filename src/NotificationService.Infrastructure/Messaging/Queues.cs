using System.Globalization;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>Queue names. Everything is published through the default exchange, straight to a queue.</summary>
public static class Queues
{
    /// <summary>Notifications waiting to be delivered.</summary>
    public const string Deliver = "notifications.deliver";

    /// <summary>Messages that ran out of attempts or could not be read.</summary>
    public const string Dead = "notifications.dead";

    /// <summary>
    /// A waiting room per delay. The delay is part of the name because RabbitMQ refuses to redeclare a queue
    /// with different arguments; a changed delay simply gets a new queue.
    /// </summary>
    public static string Retry(TimeSpan delay) =>
        string.Create(CultureInfo.InvariantCulture, $"notifications.retry.{(long)delay.TotalMilliseconds}ms");
}
