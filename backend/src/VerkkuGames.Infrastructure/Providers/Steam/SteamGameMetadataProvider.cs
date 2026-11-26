using System.Globalization;
using Microsoft.Extensions.Logging;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Infrastructure.Options;

namespace VerkkuGames.Infrastructure.Providers.Steam;

/// <summary>
/// Read-only Steam Store metadata lookup, cached for 12 hours. Failures return
/// null metadata (no invented data, no broken catalog).
/// </summary>
public sealed class SteamGameMetadataProvider : IGameMetadataProvider
{
    private const string CacheKeyPrefix = "steam:appdetails:";
    private const int CacheTtlSeconds = 12 * 60 * 60;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly ILogger<SteamGameMetadataProvider> _logger;

    public SteamGameMetadataProvider(
        HttpClient httpClient,
        ICacheService cache,
        ILogger<SteamGameMetadataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<GameMetadata?>> GetAsync(
        long steamAppId,
        CancellationToken cancellationToken = default)
    {
        if (steamAppId <= 0)
        {
            return Result<GameMetadata?>.Success(null);
        }

        var key = string.Concat(
            CacheKeyPrefix,
            steamAppId.ToString(CultureInfo.InvariantCulture));

        try
        {
            var metadata = await _cache.GetOrCreateAsync(
                key,
                async token => await FetchAsync(steamAppId, token).ConfigureAwait(false),
                TimeSpan.FromSeconds(CacheTtlSeconds),
                cancellationToken).ConfigureAwait(false);

            return Result<GameMetadata?>.Success(metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Steam metadata lookup failed for app {AppId}.", steamAppId);
            return Result<GameMetadata?>.Success(null);
        }
    }

    private async Task<GameMetadata?> FetchAsync(long steamAppId, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var url = string.Concat(
            "https://store.steampowered.com/api/appdetails?appids=",
            steamAppId.ToString(CultureInfo.InvariantCulture),
            "&cc=us&l=english");

        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        return SteamAppDetailsParser.Parse(body, steamAppId);
    }
}
