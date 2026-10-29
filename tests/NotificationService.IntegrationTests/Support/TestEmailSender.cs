using System.Collections.Concurrent;
using NotificationService.Infrastructure.Delivery;

namespace NotificationService.IntegrationTests.Support;

/// <summary>
/// Sends through the real SMTP sender to Mailpit, except for addresses that ask to fail:
/// <c>fail-…</c> fails every time, <c>flaky-…</c> fails the first time only, and <c>broken-…</c> fails until
/// a test calls <see cref="Repair"/>.
/// </summary>
public sealed class TestEmailSender(SmtpEmailSender smtp) : IEmailSender
{
    private static readonly ConcurrentDictionary<string, bool> Repaired = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public static void Repair(string address) => Repaired[address] = true;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var attempt = _attempts.AddOrUpdate(message.To, 1, (_, count) => count + 1);
        if (message.To.StartsWith("fail-", StringComparison.OrdinalIgnoreCase)
            || (message.To.StartsWith("flaky-", StringComparison.OrdinalIgnoreCase) && attempt == 1)
            || (message.To.StartsWith("broken-", StringComparison.OrdinalIgnoreCase) && !Repaired.ContainsKey(message.To)))
        {
            throw new InvalidOperationException("550 mailbox unavailable (test)");
        }

        return smtp.SendAsync(message, cancellationToken);
    }
}
