using System.Net.Mail;

namespace NotificationService.Core.Notifications;

/// <summary>Who a notification is for: the caller's own id for the person, and an email address when needed.</summary>
public sealed record Recipient(string Id, string? Email)
{
    public const int MaxIdLength = 100;
    public const int MaxEmailLength = 254;

    /// <summary>Ids are the caller's own; printable ASCII keeps them safe in URLs and logs.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= MaxIdLength } && id.All(c => c is > ' ' and < '\u007f');

    public static bool IsValidEmail(string? email) =>
        email is { Length: > 0 and <= MaxEmailLength }
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.Ordinal);
}
