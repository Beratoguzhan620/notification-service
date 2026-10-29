using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Infrastructure.Messaging;
using NotificationService.IntegrationTests.Support;
using RabbitMQ.Client;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DeliveryTests(ApiFactory factory)
{
    [Fact]
    public async Task An_email_goes_all_the_way_to_the_mailbox()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var address = Mailpit.NewAddress();

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: address, name: "Ayşe <Yılmaz>"));
        var delivered = await client.WaitForStatusAsync(accepted.Id, "Delivered");
        var mail = Assert.Single(await Waiting.UntilAsync(() => factory.MessagesToAsync(address), m => m.Count > 0));
        var full = await factory.MessageAsync(mail.Id);

        Assert.Equal(new[] { "Accepted", "Queued", "Sending", "Delivered" }, delivered.History!.Select(h => h.Status));
        Assert.Equal(1, delivered.Attempts);
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Equal("Siparişiniz yola çıktı: SIP-1001", mail.Subject);
        Assert.Equal($"{accepted.Id:N}@notification-service", mail.MessageId);
        Assert.Contains("Ayşe &lt;Yılmaz&gt;", full.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        Assert.Contains("Merhaba Ayşe <Yılmaz>,", full.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_app_notifications_need_no_address()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var accepted = await client.SendOkAsync(new
        {
            recipient = new { id = "customer-9" },
            channel = "InApp",
            template = "password-changed",
            language = "en",
            variables = new { name = "Ali", changedAt = "29 Oct 2026 10:15" },
        });
        var delivered = await client.WaitForStatusAsync(accepted.Id, "Delivered");

        Assert.Equal("Your password was changed", delivered.Subject);
    }

    [Fact]
    public async Task A_switched_off_channel_is_suppressed_and_nothing_is_sent()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var address = Mailpit.NewAddress();
        await client.PatchAsJsonAsync("/api/recipients/quiet-customer/preferences", new { email = false });

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(recipientId: "quiet-customer", email: address));
        var suppressed = await client.WaitForStatusAsync(accepted.Id, "Suppressed");
        await Task.Delay(500);

        Assert.Equal(0, suppressed.Attempts);
        Assert.Empty(await factory.MessagesToAsync(address));
    }

    [Fact]
    public async Task A_failing_address_is_retried_and_then_dead_lettered()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: Mailpit.NewAddress("fail")));
        var dead = await client.WaitForStatusAsync(accepted.Id, "DeadLettered");
        var messageId = (await factory.OutboxForAsync(accepted.Id)).Single().Id.ToString();
        var inDeadQueue = await FindInQueueAsync(Queues.Dead, messageId);

        Assert.Equal(3, dead.Attempts);
        Assert.Equal(
            new[] { "Accepted", "Queued", "Sending", "Failed", "Sending", "Failed", "Sending", "DeadLettered" },
            dead.History!.Select(h => h.Status));
        Assert.Contains("550 mailbox unavailable", dead.LastError, StringComparison.Ordinal);
        Assert.True(inDeadQueue);
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_until_it_works()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var address = Mailpit.NewAddress("flaky");

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: address));
        var delivered = await client.WaitForStatusAsync(accepted.Id, "Delivered");

        Assert.Equal(2, delivered.Attempts);
        Assert.Contains(delivered.History!, h => h.Status == "Failed");
        Assert.Null(delivered.LastError);
        Assert.Single(await Waiting.UntilAsync(() => factory.MessagesToAsync(address), m => m.Count > 0));
    }

    [Fact]
    public async Task A_redelivered_message_is_not_sent_twice()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var address = Mailpit.NewAddress();

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: address));
        var delivered = await client.WaitForStatusAsync(accepted.Id, "Delivered");
        var outbox = (await factory.OutboxForAsync(accepted.Id)).Single();

        // The same message again, as after a lost acknowledgement.
        await PublishAsync(Queues.Deliver, outbox.Id.ToString(), outbox.Payload);
        await Task.Delay(1_500);
        var after = await client.GetNotificationAsync(accepted.Id);

        Assert.Single(await factory.MessagesToAsync(address));
        Assert.Equal(delivered.History!.Count, after.History!.Count);
        Assert.Equal(1, after.Attempts);
    }

    [Fact]
    public async Task An_outbox_row_published_twice_still_sends_once()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var address = Mailpit.NewAddress();

        var accepted = await client.SendOkAsync(Notifications.OrderShipped(email: address));
        await client.WaitForStatusAsync(accepted.Id, "Delivered");
        var outboxId = (await factory.OutboxForAsync(accepted.Id)).Single().Id;

        // As if the relay had died after publishing but before marking the row.
        await factory.DbAsync(db => db.OutboxMessages.Where(m => m.Id == outboxId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.PublishedAt, (DateTimeOffset?)null)));
        var republished = await Waiting.UntilAsync(
            async () => (await factory.OutboxForAsync(accepted.Id)).Single(),
            m => m.PublishedAt is not null);
        await Task.Delay(1_500);

        Assert.Equal(2, republished.Attempts);
        Assert.Single(await factory.MessagesToAsync(address));
    }

    [Fact]
    public async Task Unreadable_messages_and_unknown_notifications_go_to_the_dead_queue()
    {
        var unreadable = Guid.NewGuid().ToString();
        var unknown = Guid.NewGuid().ToString();

        await PublishAsync(Queues.Deliver, unreadable, "this is not json");
        await PublishAsync(Queues.Deliver, unknown, $$"""{"notificationId":"{{Guid.NewGuid()}}"}""");

        Assert.True(await FindInQueueAsync(Queues.Dead, unreadable));
        Assert.True(await FindInQueueAsync(Queues.Dead, unknown));
    }

    [Fact]
    public async Task The_relay_marks_rows_published_and_notifications_queued()
    {
        var (client, created) = await factory.NewClientAsync();
        using var _ = client;
        await client.PatchAsJsonAsync("/api/recipients/batch-customer/preferences", new { inApp = false });

        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            ids.Add((await client.SendOkAsync(new
            {
                recipient = new { id = "batch-customer" },
                channel = "InApp",
                template = "password-changed",
                variables = new { name = $"N{i}", changedAt = "now" },
            })).Id);
        }

        foreach (var id in ids)
        {
            await client.WaitForStatusAsync(id, "Suppressed");
        }

        var unpublished = await factory.DbAsync(async db =>
            (await db.OutboxMessages.AsNoTracking().Where(m => m.PublishedAt == null).ToListAsync())
                .Count(m => ids.Any(id => m.Payload.Contains(id.ToString(), StringComparison.Ordinal))));
        var queuedEvents = await factory.DbAsync(db => db.NotificationEvents
            .CountAsync(e => ids.Contains(e.NotificationId) && e.Status == Core.Notifications.NotificationStatus.Queued));

        Assert.Equal(0, unpublished);
        Assert.Equal(20, queuedEvents);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    private async Task PublishAsync(string queue, string messageId, string body)
    {
        var publisher = factory.Services.GetRequiredService<Publisher>();
        await publisher.PublishAsync(queue, new BasicProperties { MessageId = messageId, Persistent = true }, Encoding.UTF8.GetBytes(body), CancellationToken.None);
    }

    /// <summary>Looks through the dead queue for one message, putting the others back.</summary>
    private async Task<bool> FindInQueueAsync(string queue, string messageId)
    {
        await using var connection = new RabbitMqConnection(factory.RabbitMqConnectionString);
        var open = await connection.GetAsync(CancellationToken.None);
        await using var channel = await open.CreateChannelAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var seen = new List<ulong>();
            var found = false;
            while (await channel.BasicGetAsync(queue, autoAck: false) is { } result)
            {
                seen.Add(result.DeliveryTag);
                if (result.BasicProperties.MessageId == messageId)
                {
                    found = true;
                    break;
                }
            }

            foreach (var tag in seen)
            {
                await channel.BasicNackAsync(tag, multiple: false, requeue: true);
            }

            if (found)
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }
}
