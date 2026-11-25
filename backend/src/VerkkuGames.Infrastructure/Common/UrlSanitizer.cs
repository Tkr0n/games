namespace VerkkuGames.Infrastructure.Common;

public static class UrlSanitizer
{
    public static string? SanitizeHttpsUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }
}
