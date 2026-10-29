using System.Security.Cryptography;
using System.Text;

namespace NotificationService.Core.Clients;

/// <summary>
/// API keys look like <c>ns_3f9a1c2b_&lt;secret&gt;</c>. The short prefix finds the client without a table scan;
/// only a SHA-256 hash of the whole key is stored. The secret is 32 random bytes, so a fast hash is enough:
/// there is nothing to guess the way there is with a password.
/// </summary>
public static class ApiKey
{
    public const string Scheme = "ns_";
    public const int PrefixLength = 8;
    public const int MinSecretLength = 32;

    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>A new key and its prefix. The key is shown to the caller once and never stored.</summary>
    public static (string Key, string Prefix) Generate()
    {
        var prefix = RandomNumberGenerator.GetString(PrefixAlphabet, PrefixLength);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return ($"{Scheme}{prefix}_{secret}", prefix);
    }

    /// <summary>The prefix of a well-formed key, or <c>null</c> for anything else.</summary>
    public static string? PrefixOf(string? key)
    {
        if (key is null
            || key.Length < Scheme.Length + PrefixLength + 1 + MinSecretLength
            || !key.StartsWith(Scheme, StringComparison.Ordinal)
            || key[Scheme.Length + PrefixLength] != '_')
        {
            return null;
        }

        var prefix = key.Substring(Scheme.Length, PrefixLength);
        if (!prefix.All(c => PrefixAlphabet.Contains(c, StringComparison.Ordinal)))
        {
            return null;
        }

        var secret = key.AsSpan(Scheme.Length + PrefixLength + 1);
        foreach (var c in secret)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            {
                return null;
            }
        }

        return prefix;
    }

    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>Compares in constant time, so the response time says nothing about how close a guess was.</summary>
    public static bool Matches(string key, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(key), storedHash);
}
