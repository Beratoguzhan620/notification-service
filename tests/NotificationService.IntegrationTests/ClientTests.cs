using System.Net;
using System.Net.Http.Json;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ClientTests(ApiFactory factory)
{
    [Fact]
    public async Task Requests_without_a_key_are_refused_with_a_hint()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/clients/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ApiKey header=\"X-Api-Key\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("ns_unknown1_this-key-was-never-issued-by-the-service")]
    [InlineData("ns_testadm1_admin-key-used-only-by-the-integration-testX")]
    public async Task Malformed_unknown_or_altered_keys_are_refused(string key)
    {
        using var client = factory.WithKey(key);

        var response = await client.GetAsync("/api/clients/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_key_from_configuration_works()
    {
        using var admin = factory.Admin();

        var me = await (await admin.GetAsync("/api/clients/me")).ReadAsync<ClientDto>();

        Assert.Equal("Operations", me.Name);
        Assert.Equal("Admin", me.Role);
        Assert.Equal("testadm1", me.KeyPrefix);
    }

    [Fact]
    public async Task A_new_key_is_shown_once_and_works_right_away()
    {
        var (client, created) = await factory.NewClientAsync();
        using var _ = client;

        var me = await (await client.GetAsync("/api/clients/me")).ReadAsync<ClientDto>();
        using var admin = factory.Admin();
        var list = await (await admin.GetAsync("/api/clients")).Content.ReadAsStringAsync();

        Assert.Equal(created.Id, me.Id);
        Assert.Equal("Sender", me.Role);
        Assert.StartsWith($"ns_{created.KeyPrefix}_", created.ApiKey, StringComparison.Ordinal);
        Assert.Contains(created.KeyPrefix, list, StringComparison.Ordinal);
        Assert.DoesNotContain(created.ApiKey, list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Senders_cannot_manage_clients()
    {
        var (sender, created) = await factory.NewClientAsync();
        using var _ = sender;

        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsJsonAsync("/api/clients", new { name = "Sneaky", role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsync($"/api/clients/{created.Id}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task A_revoked_key_stops_working()
    {
        var (sender, created) = await factory.NewClientAsync();
        using var _ = sender;
        using var admin = factory.Admin();

        var revoked = await (await admin.PostAsync($"/api/clients/{created.Id}/revoke", null)).ReadAsync<ClientDto>();
        var again = await admin.PostAsync($"/api/clients/{created.Id}/revoke", null);

        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sender.GetAsync("/api/clients/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invalid_state", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task An_admin_cannot_revoke_its_own_key()
    {
        var (admin, created) = await factory.NewClientAsync("Admin");
        using var _ = admin;

        var response = await admin.PostAsync($"/api/clients/{created.Id}/revoke", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Names_are_unique_and_required()
    {
        using var admin = factory.Admin();
        var name = $"Billing {Guid.NewGuid():N}";

        var first = await admin.PostAsJsonAsync("/api/clients", new { name });
        var second = await admin.PostAsJsonAsync("/api/clients", new { name });
        var empty = await admin.PostAsJsonAsync("/api/clients", new { name = " " });
        var unknownRole = await admin.PostAsJsonAsync("/api/clients", new { name = "Other", role = "Owner" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("name_taken", await second.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("validation_failed", await empty.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
    }

    [Fact]
    public async Task Unknown_clients_are_not_found()
    {
        using var admin = factory.Admin();

        var response = await admin.PostAsync($"/api/clients/{Guid.NewGuid()}/revoke", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await response.ProblemCodeAsync());
    }
}
