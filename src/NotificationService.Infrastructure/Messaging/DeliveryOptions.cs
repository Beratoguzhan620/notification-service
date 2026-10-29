namespace NotificationService.Infrastructure.Messaging;

public sealed class DeliveryOptions
{
    public const string Section = "Delivery";

    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// Waits before the second, third, … attempt. Configuration binding appends to arrays instead of replacing
    /// them, so the default lives in <see cref="EffectiveRetryDelays"/> and this stays empty unless configured.
    /// </summary>
    public TimeSpan[]? RetryDelays { get; set; }

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays => RetryDelays is { Length: > 0 } delays ? delays : DefaultRetryDelays;

    /// <summary>The first attempt plus one per retry delay.</summary>
    public int MaxAttempts => EffectiveRetryDelays.Count + 1;

    /// <summary>Messages a consumer holds unacknowledged at once.</summary>
    public ushort Prefetch { get; set; } = 10;

    /// <summary>Messages one worker delivers at the same time.</summary>
    public ushort Concurrency { get; set; } = 4;

    /// <summary>
    /// How long an attempt may stay in <c>Sending</c> before another worker may take it over. Covers a worker
    /// that crashed in the middle of sending; it must be longer than the SMTP timeout.
    /// </summary>
    public TimeSpan SendingLease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the outbox relay looks for new messages when it found none.</summary>
    public TimeSpan RelayInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public int RelayBatchSize { get; set; } = 50;

    /// <summary>How long dead letters stay in their queue for inspection. The database keeps the record for replay.</summary>
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(7);
}
