using System.Net;
using System.Net.Http.Json;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

public sealed record PreferencesDto(string RecipientId, bool Email, bool InApp, DateTimeOffset? UpdatedAt);

[Collection(ApiCollection.Name)]
public sealed class PreferenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_channel_is_on_until_switched_off()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var preferences = await (await client.GetAsync("/api/recipients/customer-1/preferences")).ReadAsync<PreferencesDto>();

        Assert.Equal(new PreferencesDto("customer-1", true, true, null), preferences);
    }

    [Fact]
    public async Task Only_the_given_channels_change()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        await client.PatchAsJsonAsync("/api/recipients/customer-1/preferences", new { email = false });
        var afterInApp = await (await client.PatchAsJsonAsync("/api/recipients/customer-1/preferences", new { inApp = false })).ReadAsync<PreferencesDto>();
        var afterEmail = await (await client.PatchAsJsonAsync("/api/recipients/customer-1/preferences", new { email = true })).ReadAsync<PreferencesDto>();

        Assert.False(afterInApp.Email);
        Assert.False(afterInApp.InApp);
        Assert.True(afterEmail.Email);
        Assert.False(afterEmail.InApp);
        Assert.NotNull(afterEmail.UpdatedAt);
    }

    [Fact]
    public async Task Each_client_has_its_own_recipients()
    {
        var (first, _) = await factory.NewClientAsync();
        var (second, _) = await factory.NewClientAsync();
        using var _ = first;
        using var __ = second;

        await first.PatchAsJsonAsync("/api/recipients/shared-id/preferences", new { email = false });
        var seenBySecond = await (await second.GetAsync("/api/recipients/shared-id/preferences")).ReadAsync<PreferencesDto>();

        Assert.True(seenBySecond.Email);
    }

    [Fact]
    public async Task Odd_recipient_ids_are_refused()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var response = await client.GetAsync($"/api/recipients/{new string('x', 101)}/preferences");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }
}
