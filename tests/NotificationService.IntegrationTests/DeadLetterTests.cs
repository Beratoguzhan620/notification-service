using System.Net;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DeadLetterTests(ApiFactory factory)
{
    [Fact]
    public async Task A_dead_letter_is_listed_and_a_replay_delivers_it()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        using var admin = factory.Admin();
        var address = Mailpit.NewAddress("broken");

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: address));
        await client.WaitForStatusAsync(accepted.Id, "DeadLettered");
        var listed = await (await admin.GetAsync("/api/dead-letters?limit=100")).ReadAsync<PageDto<NotificationDto>>();

        TestEmailSender.Repair(address);
        var replay = await admin.PostAsync($"/api/dead-letters/{accepted.Id}/replay", null);
        var delivered = await client.WaitForStatusAsync(accepted.Id, "Delivered");
        var outbox = await factory.OutboxForAsync(accepted.Id);

        Assert.Contains(listed.Items, n => n.Id == accepted.Id);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(1, delivered.Attempts);
        Assert.Equal(
            new[] { "DeadLettered", "Accepted", "Queued", "Sending", "Delivered" },
            delivered.History!.Select(h => h.Status).SkipWhile(s => s != "DeadLettered"));
        Assert.Equal("replayed", delivered.History!.Last(h => h.Status == "Accepted").Detail);
        Assert.Equal(2, outbox.Count);
        Assert.Single(await Waiting.UntilAsync(() => factory.MessagesToAsync(address), m => m.Count > 0));
    }

    [Fact]
    public async Task Only_dead_letters_can_be_replayed()
    {
        using var admin = factory.Admin();
        var delivered = await admin.SendOkAsync(Notifications.OrderShipped(email: Mailpit.NewAddress()));
        await admin.WaitForStatusAsync(delivered.Id, "Delivered");

        var replay = await admin.PostAsync($"/api/dead-letters/{delivered.Id}/replay", null);
        var unknown = await admin.PostAsync($"/api/dead-letters/{Guid.NewGuid()}/replay", null);

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("invalid_state", await replay.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Dead_letters_are_for_admins()
    {
        var (sender, _) = await factory.NewClientAsync();
        using var _ = sender;

        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync("/api/dead-letters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsync($"/api/dead-letters/{Guid.NewGuid()}/replay", null)).StatusCode);
    }
}
