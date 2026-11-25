using System.ComponentModel.DataAnnotations;

namespace VerkkuGames.Api.Options;

/// <summary>
/// Security settings for the public API surface. Secrets are only ever supplied
/// through environment variables or User Secrets; <c>ValidateOnStart</c> makes a
/// missing client key fail fast at boot instead of silently disabling the guard.
/// </summary>
public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    [Required]
    [MinLength(16)]
    public string ClientApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Cross-origin callers allowed to reach the API. Empty by default: the
    /// production topology is Worker-to-API server-to-server traffic only.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];

    [Range(1, 10000)]
    public int GeneralPermitLimitPerMinute { get; set; } = 120;

    [Range(1, 10000)]
    public int PricePermitLimitPerMinute { get; set; } = 30;

    [Range(1, 100000)]
    public int GlobalPermitLimitPerMinute { get; set; } = 1200;
}
