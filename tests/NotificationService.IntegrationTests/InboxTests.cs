using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class InboxTests(ApiFactory factory)
{
    [Fact]
    public async Task The_inbox_shows_delivered_in_app_notifications_newest_first_with_the_unread_count()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var ids = await DeliverInAppAsync(client, "inbox-1", 3);
        var email = await client.SendOkAsync(Notifications.OrderShipped(recipientId: "inbox-1", email: Mailpit.NewAddress()));
        await client.WaitForStatusAsync(email.Id, "Delivered");

        var inbox = await (await client.GetAsync("/api/recipients/inbox-1/inbox")).ReadAsync<InboxPageDto>();

        Assert.Equal(3, inbox.Unread);
        Assert.Equal(ids.AsEnumerable().Reverse(), inbox.Items.Select(i => i.Id));
        Assert.Equal("Your password was changed", inbox.Items[0].Subject);
        Assert.Contains("Hello N2,", inbox.Items[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_lowers_the_unread_count_and_read_all_clears_it()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var ids = await DeliverInAppAsync(client, "inbox-2", 3);

        var read = await (await client.PostAsync($"/api/recipients/inbox-2/inbox/{ids[0]}/read", null)).ReadAsync<InboxItemDto>();
        var again = await (await client.PostAsync($"/api/recipients/inbox-2/inbox/{ids[0]}/read", null)).ReadAsync<InboxItemDto>();
        var unreadOnly = await (await client.GetAsync("/api/recipients/inbox-2/inbox?unreadOnly=true")).ReadAsync<InboxPageDto>();
        var all = await (await client.PostAsync("/api/recipients/inbox-2/inbox/read-all", null)).ReadAsync<JsonElement>();
        var after = await (await client.GetAsync("/api/recipients/inbox-2/inbox")).ReadAsync<InboxPageDto>();

        Assert.NotNull(read.ReadAt);
        Assert.Equal(read.ReadAt, again.ReadAt);
        Assert.Equal(2, unreadOnly.Unread);
        Assert.DoesNotContain(unreadOnly.Items, i => i.Id == ids[0]);
        Assert.Equal(2, all.GetProperty("marked").GetInt32());
        Assert.Equal(0, after.Unread);
        Assert.All(after.Items, i => Assert.NotNull(i.ReadAt));
    }

    [Fact]
    public async Task The_inbox_pages_with_a_cursor()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var ids = await DeliverInAppAsync(client, "inbox-3", 5);

        var first = await (await client.GetAsync("/api/recipients/inbox-3/inbox?limit=2")).ReadAsync<InboxPageDto>();
        var second = await (await client.GetAsync($"/api/recipients/inbox-3/inbox?limit=2&cursor={first.NextCursor}")).ReadAsync<InboxPageDto>();
        var third = await (await client.GetAsync($"/api/recipients/inbox-3/inbox?limit=2&cursor={second.NextCursor}")).ReadAsync<InboxPageDto>();

        Assert.Equal(ids.AsEnumerable().Reverse(), first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id));
        Assert.Null(third.NextCursor);
    }

    [Fact]
    public async Task Another_client_cannot_see_or_read_the_inbox()
    {
        var (owner, _) = await factory.NewClientAsync();
        var (other, _) = await factory.NewClientAsync();
        using var _ = owner;
        using var __ = other;
        var ids = await DeliverInAppAsync(owner, "inbox-4", 1);

        var seen = await (await other.GetAsync("/api/recipients/inbox-4/inbox")).ReadAsync<InboxPageDto>();
        var read = await other.PostAsync($"/api/recipients/inbox-4/inbox/{ids[0]}/read", null);

        Assert.Empty(seen.Items);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Notifications_can_be_listed_and_filtered()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var delivered = await DeliverInAppAsync(client, "list-1", 2);
        await client.PatchAsJsonAsync("/api/recipients/list-2/preferences", new { inApp = false });
        var suppressed = await client.SendOkAsync(InApp("list-2", "x"));
        await client.WaitForStatusAsync(suppressed.Id, "Suppressed");

        var all = await (await client.GetAsync("/api/notifications")).ReadAsync<PageDto<NotificationDto>>();
        var forRecipient = await (await client.GetAsync("/api/notifications?recipientId=list-1")).ReadAsync<PageDto<NotificationDto>>();
        var bySuppressed = await (await client.GetAsync("/api/notifications?status=Suppressed")).ReadAsync<PageDto<NotificationDto>>();

        Assert.Equal(3, all.Items.Count);
        Assert.Equal(delivered.AsEnumerable().Reverse(), forRecipient.Items.Select(n => n.Id));
        Assert.Equal(suppressed.Id, Assert.Single(bySuppressed.Items).Id);
        Assert.All(all.Items, n => Assert.Null(n.History));
    }

    private static object InApp(string recipientId, string name) => new
    {
        recipient = new { id = recipientId },
        channel = "InApp",
        template = "password-changed",
        language = "en",
        variables = new { name, changedAt = "29 Oct 2026" },
    };

    /// <summary>Sends one after another and waits for each, so their order is known.</summary>
    private static async Task<List<Guid>> DeliverInAppAsync(HttpClient client, string recipientId, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var accepted = await client.SendOkAsync(InApp(recipientId, $"N{i}"));
            await client.WaitForStatusAsync(accepted.Id, "Delivered");
            ids.Add(accepted.Id);
        }

        return ids;
    }
}
