using System.Net;
using NotificationService.Api.Features.Demo;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    private const string Key = "ns_demotest_a-public-key-only-the-demo-tests-use";

    [Fact]
    public async Task Outside_the_public_demo_there_is_no_demo_key()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/demo");

        // 200 with nothing in it: a page asking learns there is no key without a failed request.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadAsStringAsync()) is "" or "null");
    }

    [Fact]
    public async Task The_published_key_becomes_a_sender_once()
    {
        await DemoEndpoints.SeedAsync(factory.Services, Key);
        // A restart finds the client and leaves it be.
        await DemoEndpoints.SeedAsync(factory.Services, Key);

        using var demo = factory.WithKey(Key);
        var me = await (await demo.GetAsync("/api/clients/me")).ReadAsync<ClientDto>();
        using var admin = factory.Admin();
        var clients = await (await admin.GetAsync("/api/clients")).Content.ReadAsStringAsync();

        Assert.Equal(DemoEndpoints.ClientName, me.Name);
        Assert.Equal("Sender", me.Role);
        Assert.Equal(1, clients.Split("demotest", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task A_key_in_the_wrong_shape_is_refused_at_start_up() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoEndpoints.SeedAsync(factory.Services, "not-a-key"));
}
