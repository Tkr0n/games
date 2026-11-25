using System.Diagnostics.CodeAnalysis;

namespace VerkkuGames.Domain.Common;

[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Error is the contractually agreed domain error value object used across every layer.")]
public sealed record Error(string Code, string Message, ErrorType Type, int? RetryAfterSeconds = null)
{
    public static Error Validation(string code, string message) =>
        new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) =>
        new(code, message, ErrorType.NotFound);

    public static Error Unauthorized(string code, string message) =>
        new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) =>
        new(code, message, ErrorType.Forbidden);

    public static Error RateLimited(string code, string message, int? retryAfterSeconds = null) =>
        new(code, message, ErrorType.RateLimited, retryAfterSeconds);

    public static Error Conflict(string code, string message, int? retryAfterSeconds = null) =>
        new(code, message, ErrorType.Conflict, retryAfterSeconds);

    public static Error ProviderUnavailable(string code, string message) =>
        new(code, message, ErrorType.ProviderUnavailable);

    public static Error PricesLocked(string code, string message) =>
        new(code, message, ErrorType.PricesLocked);
}
