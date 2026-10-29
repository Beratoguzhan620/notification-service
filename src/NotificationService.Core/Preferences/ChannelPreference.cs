using NotificationService.Core.Notifications;

namespace NotificationService.Core.Preferences;

/// <summary>Whether a recipient wants a channel. Without a row, every channel is on.</summary>
public sealed class ChannelPreference
{
    private ChannelPreference()
    {
    }

    public Guid ClientId { get; private set; }

    public string RecipientId { get; private set; } = string.Empty;

    public Channel Channel { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChannelPreference Create(Guid clientId, string recipientId, Channel channel, bool enabled, DateTimeOffset now) => new()
    {
        ClientId = clientId,
        RecipientId = recipientId,
        Channel = channel,
        Enabled = enabled,
        UpdatedAt = now,
    };

    public void Set(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
    }
}
