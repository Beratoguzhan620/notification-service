namespace NotificationService.Core.Clients;

/// <summary>A system that calls the service, identified by its API key.</summary>
public sealed class ApiClient
{
    public const int MaxNameLength = 100;

    private ApiClient()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public ClientRole Role { get; private set; }

    public string KeyPrefix { get; private set; } = string.Empty;

    public byte[] KeyHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>A client with a fresh key. The returned key is the only copy; the client keeps its hash.</summary>
    public static (ApiClient Client, string Key) Create(string name, ClientRole role, DateTimeOffset now)
    {
        var (key, _) = ApiKey.Generate();
        return (WithKey(name, role, key, now), key);
    }

    /// <summary>A client for a key chosen elsewhere, such as the first admin key from configuration.</summary>
    public static ApiClient WithKey(string name, ClientRole role, string key, DateTimeOffset now)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > MaxNameLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A client name has 2 to {MaxNameLength} characters.");
        }

        var prefix = ApiKey.PrefixOf(key)
            ?? throw new DomainException(ErrorCodes.ValidationFailed,
                $"An API key looks like '{ApiKey.Scheme}<8 lowercase letters or digits>_<at least {ApiKey.MinSecretLength} characters>'.");

        return new ApiClient
        {
            Id = Guid.CreateVersion7(now),
            Name = trimmed,
            Role = role,
            KeyPrefix = prefix,
            KeyHash = ApiKey.Hash(key),
            CreatedAt = now,
        };
    }

    public bool Accepts(string key) => IsActive && ApiKey.Matches(key, KeyHash);

    public void Revoke(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The client is already revoked.");
        }

        RevokedAt = now;
    }
}
