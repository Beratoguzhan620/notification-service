using NotificationService.Core.Clients;

namespace NotificationService.UnitTests;

public sealed class ApiKeyTests
{
    [Fact]
    public void Generated_keys_have_the_documented_shape()
    {
        var (key, prefix) = ApiKey.Generate();

        Assert.StartsWith($"ns_{prefix}_", key, StringComparison.Ordinal);
        Assert.Equal(ApiKey.PrefixLength, prefix.Length);
        Assert.Equal(prefix, ApiKey.PrefixOf(key));
        Assert.True(key.Length >= 3 + 8 + 1 + 43);
    }

    [Fact]
    public void Every_key_is_different()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => ApiKey.Generate().Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(200, keys.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("ns_short_secret")]
    [InlineData("xx_abcdefgh_0123456789012345678901234567890123")]
    [InlineData("ns_ABCDEFGH_0123456789012345678901234567890123")]
    [InlineData("ns_abcdefgh-0123456789012345678901234567890123")]
    [InlineData("ns_abcdefgh_01234567890123456789012345678901 3")]
    [InlineData("ns_abcdefgh_0123456789012345678901234567890\n3")]
    public void Malformed_keys_have_no_prefix(string? key) => Assert.Null(ApiKey.PrefixOf(key));

    [Fact]
    public void A_key_matches_its_own_hash_only()
    {
        var (key, _) = ApiKey.Generate();
        var (other, _) = ApiKey.Generate();
        var hash = ApiKey.Hash(key);

        Assert.True(ApiKey.Matches(key, hash));
        Assert.False(ApiKey.Matches(other, hash));
        Assert.Equal(32, hash.Length);
    }
}
