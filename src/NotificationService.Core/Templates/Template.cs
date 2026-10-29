using System.Text.RegularExpressions;

namespace NotificationService.Core.Templates;

/// <summary>A kind of message, such as <c>order-shipped</c>, with its versions in each language.</summary>
public sealed partial class Template
{
    public const int MaxKeyLength = 64;
    public const int MaxDescriptionLength = 300;

    private readonly List<TemplateVersion> _versions = [];

    private Template()
    {
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Used when a request asks for a language the template has no version in.</summary>
    public string DefaultLanguage { get; private set; } = Languages.English;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<TemplateVersion> Versions => _versions;

    public static Template Create(string key, string? description, string defaultLanguage, DateTimeOffset now)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        if (normalizedKey.Length > MaxKeyLength || !KeyPattern().IsMatch(normalizedKey))
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                $"A template key has 2 to {MaxKeyLength} lowercase letters, digits or '-', like 'order-shipped'.");
        }

        var language = Languages.Normalize(defaultLanguage)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, $"The default language is one of: {string.Join(", ", Languages.All)}.");

        var trimmed = description?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"The description has at most {MaxDescriptionLength} characters.");
        }

        return new Template
        {
            Id = Guid.CreateVersion7(now),
            Key = normalizedKey,
            Description = trimmed,
            DefaultLanguage = language,
            CreatedAt = now,
        };
    }

    /// <summary>Adds the next version for <paramref name="language"/>. Earlier versions stay as they were.</summary>
    public TemplateVersion AddVersion(string language, string subject, string text, string? html, DateTimeOffset now)
    {
        var normalized = Languages.Normalize(language)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, $"The language is one of: {string.Join(", ", Languages.All)}.");

        var next = (Latest(normalized)?.Version ?? 0) + 1;
        var version = TemplateVersion.Create(Id, normalized, next, subject, text, html, now);
        _versions.Add(version);
        return version;
    }

    public TemplateVersion? Latest(string language) =>
        _versions.Where(v => v.Language == language).MaxBy(v => v.Version);

    /// <summary>The newest version in the requested language, or else in the default language.</summary>
    public TemplateVersion Resolve(string? language)
    {
        var requested = Languages.Normalize(language);
        return (requested is null ? null : Latest(requested))
            ?? Latest(DefaultLanguage)
            ?? _versions.MaxBy(v => v.CreatedAt)
            ?? throw new DomainException(ErrorCodes.InvalidState, $"The template '{Key}' has no versions yet.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
