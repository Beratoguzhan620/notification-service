namespace NotificationService.Infrastructure.Persistence;

public static class ConnectionStrings
{
    /// <summary>
    /// Accepts a postgres:// URL too, as hosted databases (Neon, Render) hand them out, and turns it into the form
    /// Npgsql reads. A connection string in the usual form passes through unchanged.
    /// </summary>
    public static string? FromUrl(string? value)
    {
        if (value is null || !(value.StartsWith("postgres://", StringComparison.Ordinal) || value.StartsWith("postgresql://", StringComparison.Ordinal)))
        {
            return value;
        }

        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);
        var parts = new List<string>
        {
            $"Host={uri.Host}",
            $"Database={Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))}",
            $"Username={Uri.UnescapeDataString(credentials[0])}",
        };
        if (credentials.Length > 1)
        {
            parts.Add($"Password={Uri.UnescapeDataString(credentials[1])}");
        }

        if (uri.Port > 0)
        {
            parts.Add($"Port={uri.Port}");
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sslMode = query["sslmode"];
        if (!string.IsNullOrEmpty(sslMode))
        {
            parts.Add($"SSL Mode={sslMode switch { "require" => "Require", "verify-full" => "VerifyFull", "verify-ca" => "VerifyCA", "disable" => "Disable", _ => "Prefer" }}");
        }

        if (query["channel_binding"] is "require")
        {
            parts.Add("Channel Binding=Require");
        }

        return string.Join(';', parts);
    }
}
