using System.Security.Cryptography;
using System.Text.Json;
using NotificationService.Core.Templates;

namespace NotificationService.Api.Features.Notifications;

/// <summary>
/// A fingerprint of what was asked for, independent of JSON formatting and the order of variables, to tell a
/// retry of the same request from a different request that reuses its idempotency key.
/// </summary>
internal static class RequestHash
{
    public static byte[] Compute(SendNotificationRequest request)
    {
        var canonical = new
        {
            recipientId = request.Recipient.Id,
            email = request.Recipient.Email,
            channel = request.Channel.ToString(),
            template = request.Template.Trim().ToLowerInvariant(),
            language = Languages.Normalize(request.Language),
            variables = (request.Variables ?? []).OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new[] { v.Key, v.Value }),
        };
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical));
    }
}
