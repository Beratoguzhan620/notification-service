using System.Net.Http.Json;
using System.Text.Json;

namespace NotificationService.IntegrationTests.Support;

public sealed record MailpitMessage(string Id, string Subject, string MessageId, string To);

/// <summary>Reads what Mailpit received through its HTTP API.</summary>
public static class Mailpit
{
    public static async Task<List<MailpitMessage>> MessagesToAsync(this ApiFactory factory, string address)
    {
        using var http = new HttpClient { BaseAddress = factory.MailpitUri };
        var result = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}");
        return result.GetProperty("messages").EnumerateArray()
            .Select(m => new MailpitMessage(
                m.GetProperty("ID").GetString()!,
                m.GetProperty("Subject").GetString()!,
                m.GetProperty("MessageID").GetString()!,
                m.GetProperty("To")[0].GetProperty("Address").GetString()!))
            .ToList();
    }

    public static async Task<JsonElement> MessageAsync(this ApiFactory factory, string id)
    {
        using var http = new HttpClient { BaseAddress = factory.MailpitUri };
        return await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
    }

    /// <summary>Unique per test, so tests never see each other's mail.</summary>
    public static string NewAddress(string prefix = "customer") => $"{prefix}-{Guid.NewGuid():N}@example.com";
}
