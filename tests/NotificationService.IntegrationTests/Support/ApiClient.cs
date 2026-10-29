using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotificationService.IntegrationTests.Support;

public sealed record ClientDto(Guid Id, string Name, string Role, string KeyPrefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record CreatedClientDto(Guid Id, string Name, string Role, string KeyPrefix, DateTimeOffset CreatedAt, string ApiKey);

public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static HttpClient WithKey(this ApiFactory factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public static HttpClient Admin(this ApiFactory factory) => factory.WithKey(ApiFactory.AdminKey);

    /// <summary>Creates a client through the admin API and returns an HTTP client using its key.</summary>
    public static async Task<(HttpClient Client, CreatedClientDto Created)> NewClientAsync(this ApiFactory factory, string role = "Sender")
    {
        using var admin = factory.Admin();
        var response = await admin.PostAsJsonAsync("/api/clients", new { name = $"Test {role} {Guid.NewGuid():N}", role });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadAsync<CreatedClientDto>();
        return (factory.WithKey(created.ApiKey), created);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
