namespace NotificationService.Core.Templates;

/// <summary>The languages templates can be written in.</summary>
public static class Languages
{
    public const string Turkish = "tr";
    public const string English = "en";

    public static readonly IReadOnlyList<string> All = [Turkish, English];

    /// <summary><c>"TR"</c> → <c>"tr"</c>; anything unsupported → <c>null</c>.</summary>
    public static string? Normalize(string? language)
    {
        var lower = language?.Trim().ToLowerInvariant();
        return lower is Turkish or English ? lower : null;
    }
}
