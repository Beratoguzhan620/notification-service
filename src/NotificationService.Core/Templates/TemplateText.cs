using System.Net;
using System.Text;

namespace NotificationService.Core.Templates;

/// <summary>
/// Text with <c>{{ variable }}</c> placeholders. Parsed once when a version is saved, so a broken template is
/// refused up front instead of failing later in a worker.
/// </summary>
public sealed class TemplateText
{
    public const int MaxVariableNameLength = 64;

    private readonly IReadOnlyList<Segment> _segments;

    private TemplateText(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
        Variables = segments.Where(s => s.IsVariable).Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> Variables { get; }

    /// <summary>Parses <paramref name="source"/>, naming the field and position of the first mistake.</summary>
    public static TemplateText Parse(string source, string field)
    {
        var segments = new List<Segment>();
        var position = 0;
        while (position < source.Length)
        {
            var open = source.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                segments.Add(Segment.Literal(source[position..]));
                break;
            }

            if (open > position)
            {
                segments.Add(Segment.Literal(source[position..open]));
            }

            var close = source.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                throw Invalid(field, open, "A '{{' has no matching '}}'.");
            }

            var name = source[(open + 2)..close].Trim();
            if (!IsValidName(name))
            {
                throw Invalid(field, open,
                    $"'{{{{{name}}}}}' is not a variable name. Use a letter followed by letters, digits or '_', at most {MaxVariableNameLength} characters.");
            }

            segments.Add(Segment.Variable(name));
            position = close + 2;
        }

        return new TemplateText(segments);
    }

    /// <summary>
    /// Fills in the variables. In HTML every value is encoded, so a value can never add markup or scripts.
    /// Callers check for missing variables first; a missing one here is a programming error.
    /// </summary>
    public string Render(IReadOnlyDictionary<string, string> values, TextKind kind)
    {
        var builder = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (!segment.IsVariable)
            {
                builder.Append(segment.Value);
                continue;
            }

            var value = values[segment.Value];
            builder.Append(kind switch
            {
                TextKind.Html => WebUtility.HtmlEncode(value),
                // A subject is a single mail header line: a value must not be able to start another header.
                TextKind.SingleLine => value.ReplaceLineEndings(" "),
                _ => value,
            });
        }

        return builder.ToString();
    }

    private static bool IsValidName(string name) =>
        name.Length is > 0 and <= MaxVariableNameLength
        && char.IsAsciiLetter(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static DomainException Invalid(string field, int position, string message) =>
        new(ErrorCodes.InvalidTemplate, $"{field}: {message}")
        {
            Details = { ["field"] = field, ["position"] = position },
        };

    private readonly record struct Segment(string Value, bool IsVariable)
    {
        public static Segment Literal(string value) => new(value, false);

        public static Segment Variable(string name) => new(name, true);
    }
}

public enum TextKind
{
    Plain,
    SingleLine,
    Html,
}
