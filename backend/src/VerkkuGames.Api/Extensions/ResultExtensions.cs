using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using VerkkuGames.Domain.Common;

namespace VerkkuGames.Api.Extensions;

/// <summary>
/// Single place where domain <see cref="Result{T}"/> failures become RFC 9457
/// <c>ProblemDetails</c>. Messages are scrubbed of the wholesale provider brand
/// before anything is written to the wire.
/// </summary>
public static partial class ResultExtensions
{
    private const string DefaultErrorCode = "request_failed";

    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFor(result.Error, httpContext);
    }

    public static IResult ProblemFor(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var (status, title) = MapStatus(error.Type);
        var code = string.IsNullOrWhiteSpace(error.Code) ? DefaultErrorCode : error.Code;

        return Problem(
            httpContext,
            status,
            title,
            error.Message,
            code,
            error.RetryAfterSeconds);
    }

    public static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        Problem(httpContext, StatusCodes.Status400BadRequest, "Validation Failed", detail, "validation_failed", null);

    public static IResult Problem(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string code,
        int? retryAfterSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (retryAfterSeconds is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Max(0, retryAfter).ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(
            CreateProblem(status, title, detail, code, GetTraceId(httpContext), retryAfterSeconds));
    }

    public static ProblemDetails CreateProblem(
        int status,
        string title,
        string detail,
        string code,
        string traceId,
        int? retryAfterSeconds = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = Sanitize(title),
            Detail = Sanitize(detail),
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;

        if (retryAfterSeconds is { } retryAfter)
        {
            problem.Extensions["retryAfterSeconds"] = Math.Max(0, retryAfter);
        }

        return problem;
    }

    public static string GetTraceId(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }

    /// <summary>
    /// Defense in depth: nothing that resembles the wholesale provider brand may
    /// ever reach a public response, even through a future error message.
    /// </summary>
    public static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : ProviderNamePattern().Replace(value, "the catalog service");

    private static (int Status, string Title) MapStatus(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation Failed"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.PricesLocked => (StatusCodes.Status422UnprocessableEntity, "Prices Locked"),
            ErrorType.RateLimited => (StatusCodes.Status429TooManyRequests, "Too Many Requests"),
            ErrorType.ProviderUnavailable => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

    [GeneratedRegex("shop\\s*2\\s*top\\s*up", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProviderNamePattern();
}
