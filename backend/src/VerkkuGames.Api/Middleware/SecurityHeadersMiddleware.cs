namespace VerkkuGames.Api.Middleware;

/// <summary>
/// Emits conservative security headers on every response and removes the
/// server-identifying header if a hosting layer adds it.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        _ = headers.Remove("Server");
        _ = headers.Remove("X-Powered-By");

        await next(context).ConfigureAwait(false);

        // Kestrel may append the header at the very end of the response; strip it
        // again when the response is still editable so no hosting layer can
        // reintroduce it.
        if (!context.Response.HasStarted)
        {
            _ = headers.Remove("Server");
        }
    }
}
