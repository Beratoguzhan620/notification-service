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

    /// <summary>How long dead letters stay in their queue for inspection. The database keeps the record for replay.</summary>
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(7);
}
