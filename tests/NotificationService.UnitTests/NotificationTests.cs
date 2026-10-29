using NotificationService.Core;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;

namespace NotificationService.UnitTests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_notification_is_accepted_with_its_first_history_entry()
    {
        var notification = New(Channel.Email);

        Assert.Equal(NotificationStatus.Accepted, notification.Status);
        Assert.Equal("<b>Ayşe</b>", notification.Html);
        var entry = Assert.Single(notification.Events);
        Assert.Equal(NotificationStatus.Accepted, entry.Status);
        Assert.Equal("welcome v1 (en)", entry.Detail);
    }

    [Fact]
    public void In_app_notifications_keep_no_html() => Assert.Null(New(Channel.InApp).Html);

    [Fact]
    public void A_successful_delivery_walks_through_every_status()
    {
        var notification = New(Channel.Email);

        notification.MarkQueued(Now.AddSeconds(1));
        Assert.True(notification.StartAttempt(Now.AddSeconds(2)));
        notification.MarkDelivered(Now.AddSeconds(3));

        Assert.Equal(
            new[] { NotificationStatus.Accepted, NotificationStatus.Queued, NotificationStatus.Sending, NotificationStatus.Delivered },
            notification.Events.Select(e => e.Status));
        Assert.Equal(1, notification.Attempts);
        Assert.Equal(Now.AddSeconds(3), notification.DeliveredAt);
    }

    [Fact]
    public void Failures_retry_until_the_last_attempt_and_then_dead_letter()
    {
        var notification = New(Channel.Email);

        notification.StartAttempt(Now);
        notification.MarkFailed("SMTP timeout", willRetry: true, Now);
        notification.StartAttempt(Now);
        notification.MarkFailed("SMTP timeout", willRetry: false, Now);

        Assert.Equal(NotificationStatus.DeadLettered, notification.Status);
        Assert.Equal(2, notification.Attempts);
        Assert.Equal("SMTP timeout", notification.LastError);
        Assert.False(notification.StartAttempt(Now));
    }

    [Fact]
    public void A_delivered_notification_is_not_delivered_again()
    {
        var notification = New(Channel.Email);
        notification.StartAttempt(Now);
        notification.MarkDelivered(Now);

        Assert.False(notification.StartAttempt(Now));
        Assert.Equal(1, notification.Attempts);
    }

    [Fact]
    public void Replay_starts_over_from_a_dead_letter_only()
    {
        var notification = New(Channel.Email);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => notification.Replay(Now)).Code);

        notification.StartAttempt(Now);
        notification.MarkFailed("boom", willRetry: false, Now);
        notification.Replay(Now);

        Assert.Equal(NotificationStatus.Accepted, notification.Status);
        Assert.Equal(0, notification.Attempts);
        Assert.Equal("replayed", notification.Events[^1].Detail);
    }

    [Fact]
    public void Only_delivered_in_app_notifications_can_be_read_and_the_first_read_counts()
    {
        var email = New(Channel.Email);
        email.StartAttempt(Now);
        email.MarkDelivered(Now);
        Assert.Throws<DomainException>(() => email.MarkRead(Now));

        var inApp = New(Channel.InApp);
        Assert.Throws<DomainException>(() => inApp.MarkRead(Now));
        inApp.StartAttempt(Now);
        inApp.MarkDelivered(Now);
        inApp.MarkRead(Now.AddMinutes(1));
        inApp.MarkRead(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), inApp.ReadAt);
    }

    [Fact]
    public void Long_errors_are_shortened()
    {
        var notification = New(Channel.Email);
        notification.StartAttempt(Now);

        notification.MarkFailed(new string('x', 2_000), willRetry: true, Now);

        Assert.Equal(Notification.MaxErrorLength, notification.LastError!.Length);
    }

    [Theory]
    [InlineData("customer-42", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("ümit", false)]
    public void Recipient_ids_are_printable_ascii(string id, bool valid) => Assert.Equal(valid, Recipient.IsValidId(id));

    [Theory]
    [InlineData("ayse@example.com", true)]
    [InlineData("Ayşe <ayse@example.com>", false)]
    [InlineData("not-an-address", false)]
    public void Email_addresses_are_bare_addresses(string email, bool valid) => Assert.Equal(valid, Recipient.IsValidEmail(email));

    private static Notification New(Channel channel)
    {
        var template = Template.Create("welcome", null, "en", Now);
        var version = template.AddVersion("en", "Hi {{name}}", "Hello {{name}}", "<b>{{name}}</b>", Now);
        var message = version.Render(new Dictionary<string, string> { ["name"] = "Ayşe" });
        return Notification.Accept(Guid.NewGuid(), "key-1", [1, 2, 3], new Recipient("customer-42", "ayse@example.com"), channel,
            template, version, message, Now);
    }
}
