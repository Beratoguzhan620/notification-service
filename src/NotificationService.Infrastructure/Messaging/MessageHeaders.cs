using System.Text;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>Headers this service puts on its messages.</summary>
public static class MessageHeaders
{
    /// <summary>The W3C trace context, so a trace continues from the API through RabbitMQ into the worker.</summary>
    public const string TraceParent = "traceparent";

    /// <summary>How often the message has been handed to a consumer by this service (for people reading the queue).</summary>
    public const string Attempt = "x-attempt";

    /// <summary>Why a message was dead-lettered or sent for a retry.</summary>
    public const string Reason = "x-reason";

    /// <summary>Header values arrive as bytes from the broker.</summary>
    public static string? ReadString(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => value?.ToString(),
            }
            : null;

    public static int ReadInt(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
                _ => 0,
            }
            : 0;
}
