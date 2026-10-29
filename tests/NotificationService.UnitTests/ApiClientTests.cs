using NotificationService.Core;
using NotificationService.Core.Clients;

namespace NotificationService.UnitTests;

public sealed class ApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_client_accepts_its_key_and_stores_only_a_hash()
    {
        var (client, key) = ApiClient.Create("  Shop backend ", ClientRole.Sender, Now);

        Assert.Equal("Shop backend", client.Name);
        Assert.True(client.Accepts(key));
        Assert.Equal(ApiKey.PrefixOf(key), client.KeyPrefix);
        Assert.DoesNotContain(key, Convert.ToBase64String(client.KeyHash), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" x ")]
    public void Names_need_two_characters(string name)
    {
        var error = Assert.Throws<DomainException>(() => ApiClient.Create(name, ClientRole.Sender, Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_configured_key_must_have_the_documented_shape()
    {
        var error = Assert.Throws<DomainException>(() => ApiClient.WithKey("Operations", ClientRole.Admin, "secret", Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_revoked_client_accepts_nothing_and_cannot_be_revoked_again()
    {
        var (client, key) = ApiClient.Create("Shop backend", ClientRole.Sender, Now);

        client.Revoke(Now.AddMinutes(1));

        Assert.False(client.Accepts(key));
        Assert.Equal(Now.AddMinutes(1), client.RevokedAt);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => client.Revoke(Now)).Code);
    }
}
