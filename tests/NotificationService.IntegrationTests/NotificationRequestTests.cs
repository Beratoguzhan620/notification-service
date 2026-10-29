using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class NotificationRequestTests(ApiFactory factory)
{
    [Fact]
    public async Task A_request_is_accepted_with_its_outbox_message_in_the_same_transaction()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var response = await client.SendAsync(Notifications.OrderShipped());
        var notification = await response.ReadAsync<NotificationDto>();
        var outbox = await factory.OutboxForAsync(notification.Id);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/api/notifications/{notification.Id}", response.Headers.Location!.ToString());
        Assert.Equal("Accepted", notification.Status);
        Assert.Equal(new TemplateRefDto("order-shipped", 1, "tr"), notification.Template);
        Assert.Equal("Siparişiniz yola çıktı: SIP-1001", notification.Subject);
        Assert.Equal("Accepted", Assert.Single(notification.History!).Status);
        var message = Assert.Single(outbox);
        Assert.Equal("deliver-notification", message.Type);
        Assert.Null(message.PublishedAt);
    }

    [Fact]
    public async Task The_same_key_and_body_returns_the_same_notification()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(Notifications.OrderShipped(), key);
        var second = await client.SendAsync(Notifications.OrderShipped(), key);
        var firstId = (await first.ReadAsync<NotificationDto>()).Id;
        var secondId = (await second.ReadAsync<NotificationDto>()).Id;
        var rows = (await factory.OutboxForAsync(firstId)).Count;

        Assert.Equal(firstId, secondId);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Variable_order_and_formatting_do_not_change_the_request()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var key = Guid.NewGuid().ToString();
        var reordered = new
        {
            channel = "Email",
            template = "Order-Shipped",
            language = "TR",
            recipient = new { email = "ayse@example.com", id = "customer-42" },
            variables = new Dictionary<string, string>
            {
                ["trackingNo"] = "YK123456789",
                ["carrier"] = "Yurtiçi Kargo",
                ["orderNo"] = "SIP-1001",
                ["name"] = "Ayşe",
            },
        };

        var first = await client.SendOkAsync(Notifications.OrderShipped(), key);
        var second = await client.SendAsync(reordered, key);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(first.Id, (await second.ReadAsync<NotificationDto>()).Id);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_refused()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var key = Guid.NewGuid().ToString();

        var first = await client.SendOkAsync(Notifications.OrderShipped(name: "Ayşe"), key);
        var second = await client.SendAsync(Notifications.OrderShipped(name: "Ali"), key);
        var problem = await second.ReadAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("idempotency_key_reused", problem.GetProperty("code").GetString());
        Assert.Equal(first.Id, problem.GetProperty("notificationId").GetGuid());
    }

    [Fact]
    public async Task Keys_belong_to_one_client()
    {
        var (first, _) = await factory.NewClientAsync();
        var (second, _) = await factory.NewClientAsync();
        using var _ = first;
        using var __ = second;
        var key = Guid.NewGuid().ToString();

        var a = await first.SendOkAsync(Notifications.OrderShipped(), key);
        var b = await second.SendOkAsync(Notifications.OrderShipped(name: "Ali"), key);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Simultaneous_identical_requests_make_one_notification()
    {
        var (client, created) = await factory.NewClientAsync();
        using var _ = client;
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.SendAsync(Notifications.OrderShipped(), key)));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.ReadAsync<NotificationDto>()).Id));
        var stored = await factory.DbAsync(db => db.Notifications.CountAsync(n => n.ClientId == created.Id && n.IdempotencyKey == key));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task A_missing_language_falls_back_to_the_template_default()
    {
        using var admin = factory.Admin();
        var key = $"t-{Guid.NewGuid():N}"[..20];
        await admin.PostAsJsonAsync("/api/templates", new { key, description = "English only", defaultLanguage = "en" });
        await admin.PostAsJsonAsync($"/api/templates/{key}/versions", new { language = "en", subject = "Hi {{name}}", text = "Hello {{name}}" });

        var notification = await admin.SendOkAsync(new
        {
            recipient = new { id = "customer-7" },
            channel = "InApp",
            template = key,
            language = "tr",
            variables = new { name = "Ayşe" },
        });

        Assert.Equal(new TemplateRefDto(key, 1, "en"), notification.Template);
        Assert.Equal("Hi Ayşe", notification.Subject);
    }

    [Fact]
    public async Task Rendered_html_escapes_values_and_the_subject_stays_one_line()
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var notification = await client.SendOkAsync(Notifications.OrderShipped(name: "<img src=x onerror=alert(1)>\r\nBcc: x@example.com"));
        var stored = await factory.DbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id));

        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", stored.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", stored.Html, StringComparison.Ordinal);
        Assert.Contains("<img src=x", stored.Text, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', stored.Subject);
    }

    [Theory]
    [InlineData(null, "Idempotency-Key")]
    [InlineData("has space", "Idempotency-Key")]
    public async Task An_idempotency_key_is_required(string? key, string field)
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications") { Content = JsonContent.Create(Notifications.OrderShipped()) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Email", null, "recipient.email")]
    [InlineData("Email", "not-an-address", "recipient.email")]
    [InlineData("Sms", "ayse@example.com", null)]
    public async Task Requests_are_validated(string channel, string? email, string? field)
    {
        var (client, _) = await factory.NewClientAsync();
        using var _ = client;

        var response = await client.SendAsync(new
        {
            recipient = new { id = "customer-42", email },
            channel,
            template = "order-shipped",
            variables = new { name = "x", orderNo = "1", carrier = "c", trackingNo = "t" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (field is not null)
        {
            Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Missing_variables_and_unknown_templates_are_refused_before_anything_is_saved()
    {
        var (client, created) = await factory.NewClientAsync();
        using var _ = client;

        var missing = await client.SendAsync(new { recipient = new { id = "c-1", email = "a@example.com" }, channel = "Email", template = "order-shipped" });
        var unknown = await client.SendAsync(new { recipient = new { id = "c-1" }, channel = "InApp", template = "no-such-template" });
        var stored = await factory.DbAsync(db => db.Notifications.CountAsync(n => n.ClientId == created.Id));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal("missing_variables", await missing.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Senders_see_only_their_own_notifications_and_admins_see_all()
    {
        var (owner, _) = await factory.NewClientAsync();
        var (other, _) = await factory.NewClientAsync();
        using var _ = owner;
        using var __ = other;
        using var admin = factory.Admin();

        var notification = await owner.SendOkAsync(Notifications.OrderShipped());

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/notifications/{notification.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/notifications/{notification.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/notifications/{notification.Id}")).StatusCode);
    }
}
