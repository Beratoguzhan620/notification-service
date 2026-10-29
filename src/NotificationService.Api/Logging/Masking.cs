namespace NotificationService.Api.Logging;

/// <summary>Shortens personal data to what is still useful when reading logs.</summary>
internal static class Masking
{
    public const string Hidden = "***";

    /// <summary><c>ayse.yilmaz@example.com</c> → <c>a***@example.com</c>.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Hidden;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Hidden : $"{email[0]}{Hidden}{email[at..]}";
    }
}
