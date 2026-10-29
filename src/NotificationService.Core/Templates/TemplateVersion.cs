namespace NotificationService.Core.Templates;

/// <summary>
/// One version of a template in one language. Versions are never changed, so a notification can always say
/// exactly which text it was made from.
/// </summary>
public sealed class TemplateVersion
{
    public const int MaxSubjectLength = 200;
    public const int MaxTextLength = 20_000;
    public const int MaxHtmlLength = 100_000;
    public const int MaxValueLength = 2_000;

    private TemplateText? _subject;
    private TemplateText? _text;
    private TemplateText? _html;

    private TemplateVersion()
    {
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public string Language { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public string? Html { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Every variable used in the subject, the text or the HTML, sorted.</summary>
    public IReadOnlyList<string> Variables =>
        Parsed().SelectMany(t => t.Variables).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    internal static TemplateVersion Create(Guid templateId, string language, int version, string subject, string text, string? html, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || subject.Contains('\n') || subject.Contains('\r'))
        {
            errors.Add($"The subject is one line of 1 to {MaxSubjectLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            errors.Add($"The text has 1 to {MaxTextLength} characters.");
        }

        if (html is not null && (string.IsNullOrWhiteSpace(html) || html.Length > MaxHtmlLength))
        {
            errors.Add($"The HTML, when given, has 1 to {MaxHtmlLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, string.Join(' ', errors));
        }

        var created = new TemplateVersion
        {
            Id = Guid.CreateVersion7(now),
            TemplateId = templateId,
            Language = language,
            Version = version,
            Subject = subject.Trim(),
            Text = text,
            Html = html,
            CreatedAt = now,
        };
        _ = created.Parsed();
        return created;
    }

    /// <summary>Fills in the variables, or names every missing one. Unknown extra variables are ignored.</summary>
    public RenderedMessage Render(IReadOnlyDictionary<string, string> values)
    {
        var missing = Variables.Where(v => !values.TryGetValue(v, out var value) || value is null).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException(ErrorCodes.MissingVariables, $"Values are missing for: {string.Join(", ", missing)}.")
            {
                Details = { ["missing"] = missing },
            };
        }

        var tooLong = Variables.Where(v => values[v].Length > MaxValueLength).ToList();
        if (tooLong.Count > 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Values have at most {MaxValueLength} characters: {string.Join(", ", tooLong)}.");
        }

        var (subject, text, html) = ParsedParts();
        return new RenderedMessage(
            subject.Render(values, TextKind.SingleLine),
            text.Render(values, TextKind.Plain),
            html?.Render(values, TextKind.Html));
    }

    private IEnumerable<TemplateText> Parsed()
    {
        var (subject, text, html) = ParsedParts();
        return html is null ? [subject, text] : [subject, text, html];
    }

    private (TemplateText Subject, TemplateText Text, TemplateText? Html) ParsedParts()
    {
        _subject ??= TemplateText.Parse(Subject, "subject");
        _text ??= TemplateText.Parse(Text, "text");
        _html ??= Html is null ? null : TemplateText.Parse(Html, "html");
        return (_subject, _text, _html);
    }
}
