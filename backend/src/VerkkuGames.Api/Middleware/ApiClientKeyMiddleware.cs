using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VerkkuGames.Api.Extensions;
using VerkkuGames.Api.Options;

namespace VerkkuGames.Api.Middleware;

/// <summary>
/// Authenticates the Cloudflare Worker (BFF) before any rate limiter or endpoint
/// runs. <c>X-Forwarded-Client-Ip</c> is only trusted after this middleware
/// marks the request as authenticated.
/// </summary>
public sealed class ApiClientKeyMiddleware(
    RequestDelegate next,
    IOptions<ApiSecurityOptions> options,
    ILogger<ApiClientKeyMiddleware> logger)
{
    public const string AuthenticatedBffItemKey = "VerkkuGames.AuthenticatedBff";
    public const string ClientApiKeyHeaderName = "X-Api-Client-Key";
    public const string ForwardedClientIpHeaderName = "X-Forwarded-Client-Ip";

    private const string ProtectedPathPrefix = "/api/v1";
    private const int MaxClientIpLength = 64;

    private readonly byte[] _expectedKeyBytes = Encoding.UTF8.GetBytes(options.Value.ClientApiKey);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(ProtectedPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var providedKey = context.Request.Headers[ClientApiKeyHeaderName].ToString();

        if (!FixedTimeEquals(providedKey))
        {
            logger.LogWarning(
                "Rejected unauthenticated API request for {Path}.",
                context.Request.Path.Value);

            var problem = ResultExtensions.CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Valid client API key is required.",
                "unauthorized",
                ResultExtensions.GetTraceId(context));

            await Results.Problem(problem).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        context.Items[AuthenticatedBffItemKey] = true;
        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Rate-limit partition key. The forwarded visitor IP is honored only when
    /// this request already passed the BFF key check; otherwise the transport
    /// address is used (and unauthenticated requests never reach this point).
    /// </summary>
    public static string ResolvePartitionKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(AuthenticatedBffItemKey, out var authenticated)
            && authenticated is true)
        {
            var forwarded = SanitizeClientIp(
                context.Request.Headers[ForwardedClientIpHeaderName].ToString());

            if (forwarded.Length > 0)
            {
                return forwarded;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string SanitizeClientIp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxClientIpLength)
        {
            return string.Empty;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not (':' or '.' or '-' or '_' or '%'))
            {
                return string.Empty;
            }
        }

        return trimmed;
    }

    private bool FixedTimeEquals(string provided)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = _expectedKeyBytes;
        var maxLength = Math.Max(providedBytes.Length, expectedBytes.Length);

        if (maxLength == 0)
        {
            return true;
        }

        // Zero-pad both keys to the same length so the comparison never throws
        // and never short-circuits on length; the explicit length check only
        // rejects trailing padding matches and reveals no secret bytes.
        var providedPadded = new byte[maxLength];
        var expectedPadded = new byte[maxLength];
        providedBytes.CopyTo(providedPadded, 0);
        expectedBytes.CopyTo(expectedPadded, 0);

        var contentEquals = CryptographicOperations.FixedTimeEquals(providedPadded, expectedPadded);
        return contentEquals && providedBytes.Length == expectedBytes.Length;
    }
}
