using NotificationService.Core.Notifications;

namespace NotificationService.Api.Features.Notifications;

public sealed record RecipientDto(string Id, string? Email = null);

public sealed record TemplateRef(string Key, int Version, string Language);

public sealed record HistoryEntry(NotificationStatus Status, DateTimeOffset OccurredAt, string? Detail);

public sealed record NotificationResponse(
    Guid Id,
    NotificationStatus Status,
    Channel Channel,
    RecipientDto Recipient,
    TemplateRef Template,
    string Subject,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    string? LastError,
    List<HistoryEntry>? History)
{
    public static NotificationResponse Create(Notification n, bool withHistory) => new(
        n.Id, n.Status, n.Channel,
        new RecipientDto(n.RecipientId, n.RecipientEmail),
        new TemplateRef(n.TemplateKey, n.TemplateVersion, n.Language),
        n.Subject, n.Attempts, n.CreatedAt, n.UpdatedAt, n.DeliveredAt, n.ReadAt, n.LastError,
        withHistory ? n.Events.OrderBy(e => e.Id).Select(e => new HistoryEntry(e.Status, e.OccurredAt, e.Detail)).ToList() : null);
}
