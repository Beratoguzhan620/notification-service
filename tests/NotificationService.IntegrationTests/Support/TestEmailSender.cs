using System.Collections.Concurrent;
using NotificationService.Infrastructure.Delivery;

namespace NotificationService.IntegrationTests.Support;

/// <summary>
/// Sends through the real SMTP sender to Mailpit, except for addresses that ask to fail:
/// <c>fail-…</c> fails every time, <c>flaky-…</c> fails the first time only.
/// </summary>
public sealed class TestEmailSender(SmtpEmailSender smtp) : IEmailSender
{
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var attempt = _attempts.AddOrUpdate(message.To, 1, (_, count) => count + 1);
        if (message.To.StartsWith("fail-", StringComparison.OrdinalIgnoreCase)
            || (message.To.StartsWith("flaky-", StringComparison.OrdinalIgnoreCase) && attempt == 1))
        {
            throw new InvalidOperationException("550 mailbox unavailable (test)");
        }

        return smtp.SendAsync(message, cancellationToken);
    }
}
