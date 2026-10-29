namespace NotificationService.Infrastructure.Delivery;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary>Use TLS when the server offers it. Mailpit in Compose does not, which is fine on a private network.</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "Notification Service <no-reply@notifications.local>";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
