using System.Net.Http.Json;
using System.Text.Json;

namespace NotificationService.IntegrationTests.Support;

public sealed record RecipientDto(string Id, string? Email);

public sealed record TemplateRefDto(string Key, int Version, string Language);

public sealed record HistoryEntryDto(string Status, DateTimeOffset OccurredAt, string? Detail);

public sealed record NotificationDto(Guid Id, string Status, string Channel, RecipientDto Recipient, TemplateRefDto Template, string Subject,
    int Attempts, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt, string? LastError,
    List<HistoryEntryDto>? History);

public static class Notifications
{
    /// <summary>An order-shipped email in Turkish, the sample template every test stack has.</summary>
    public static object OrderShipped(string recipientId = "customer-42", string email = "ayse@example.com", string channel = "Email",
        string language = "tr", string name = "Ayşe") => new
        {
            recipient = new { id = recipientId, email },
            channel,
            template = "order-shipped",
            language,
            variables = new Dictionary<string, string>
            {
                ["name"] = name,
                ["orderNo"] = "SIP-1001",
                ["carrier"] = "Yurtiçi Kargo",
                ["trackingNo"] = "YK123456789",
            },
        };

    public static Task<HttpResponseMessage> SendAsync(this HttpClient client, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<NotificationDto> SendOkAsync(this HttpClient client, object body, string? idempotencyKey = null)
    {
        var response = await client.SendAsync(body, idempotencyKey);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.ReadAsync<NotificationDto>();
    }

    public static async Task<NotificationDto> GetNotificationAsync(this HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/notifications/{id}")).ReadAsync<NotificationDto>();

    public static string Json(object value) => JsonSerializer.Serialize(value, ApiClient.Json);
}
