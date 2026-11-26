using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure.Options;

namespace VerkkuGames.Infrastructure.Providers.FazerCards;

/// <summary>
/// Read-only catalog provider for full game keys (FazerCards REST API v2).
/// Only reads are implemented in v1 — no order creation yet.
/// </summary>
public sealed class FazerCardsCatalogProvider : ICatalogProvider
{
    private const string GameKeysPath = "gamekeys";
    private const string MePath = "me";
    private const int MaxCursorPages = 10;
    private const int UnfilteredGameLimit = 8;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly FazerCardsOptions _options;
    private readonly CacheOptions _cacheOptions;
    private readonly IPricingService _pricingService;
    private readonly IRegionResolver _regionResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<FazerCardsCatalogProvider> _logger;
    private readonly Uri _baseAddress;

    public FazerCardsCatalogProvider(
        HttpClient httpClient,
        IOptions<FazerCardsOptions> options,
        IOptions<CacheOptions> cacheOptions,
        IPricingService pricingService,
        IRegionResolver regionResolver,
        ICacheService cache,
        ILogger<FazerCardsCatalogProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cacheOptions);
        ArgumentNullException.ThrowIfNull(pricingService);
        ArgumentNullException.ThrowIfNull(regionResolver);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _cacheOptions = cacheOptions.Value;
        _pricingService = pricingService;
        _regionResolver = regionResolver;
        _cache = cache;
        _logger = logger;
        _baseAddress = new Uri(string.Concat(_options.BaseUrl.TrimEnd('/'), "/"), UriKind.Absolute);
    }

    public string InternalKey => "fazercards";

    public Guid ProviderUid => _options.ProviderUid;

    public Task<Result<IReadOnlyList<CatalogBigCategory>>> GetBigCategoriesAsync(
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey("big-categories"),
            _cacheOptions.BigCategoriesSeconds,
            FetchBigCategoriesAsync,
            cancellationToken);

    public Task<Result<IReadOnlyList<CatalogCategory>>> GetCategoriesAsync(
        string? bigCategoryId = null,
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey(string.Concat("categories:", NormalizeKeySegment(bigCategoryId))),
            _cacheOptions.CategoriesSeconds,
            token => FetchCategoriesAsync(bigCategoryId, token),
            cancellationToken);

    public Task<Result<IReadOnlyList<CatalogItem>>> GetItemsAsync(
        string? categoryId = null,
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey(string.Concat("items:", NormalizeKeySegment(categoryId))),
            _cacheOptions.ItemsSeconds,
            token => FetchItemsAsync(categoryId, token),
            cancellationToken);

    public async Task<Result<CatalogItem>> GetItemByIdAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var (gameId, keyId) = SplitItemId(itemId);
        if (gameId is null || keyId is null)
        {
            return Result<CatalogItem>.Failure(
                Error.NotFound("item_not_found", "The requested catalog item was not found."));
        }

        var itemsResult = await GetItemsAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (itemsResult.IsFailure)
        {
            return Result<CatalogItem>.Failure(itemsResult.Error);
        }

        var match = itemsResult.Value.FirstOrDefault(candidate =>
            string.Equals(candidate.ExternalId, itemId, StringComparison.Ordinal));

        return match is null
            ? Result<CatalogItem>.Failure(Error.NotFound("item_not_found", "The requested catalog item was not found."))
            : Result<CatalogItem>.Success(match);
    }

    public Task<Result<ItemPriceQuote>> GetItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey(string.Concat("price:", NormalizeKeySegment(itemId))),
            _cacheOptions.ItemPriceSeconds,
            token => FetchItemPriceAsync(itemId, token),
            cancellationToken);

    public Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey(string.Concat("requirements:", NormalizeKeySegment(categoryId))),
            _cacheOptions.RequirementsSeconds,
            token => FetchRequirementsAsync(categoryId, token),
            cancellationToken);

    public Task<Result<bool>> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey("account"),
            _cacheOptions.AccountHealthSeconds,
            FetchAccountHealthAsync,
            cancellationToken);

    // ------------------------------------------------------------------ catalog

    private async Task<Result<IReadOnlyList<CatalogBigCategory>>> FetchBigCategoriesAsync(
        CancellationToken cancellationToken)
    {
        var games = await FetchAllGamesAsync(cancellationToken).ConfigureAwait(false);
        if (games.IsFailure)
        {
            return Result<IReadOnlyList<CatalogBigCategory>>.Failure(games.Error);
        }

        var mapped = games.Value.Items
            .Select(game => new CatalogBigCategory(
                _options.ProviderUid,
                ReadId(game.GameId),
                game.Name ?? string.Empty,
                BuildDescription(game),
                null))
            .Where(item => item.ExternalId.Length > 0)
            .ToArray();

        return Result<IReadOnlyList<CatalogBigCategory>>.Success(mapped);
    }

    private async Task<Result<IReadOnlyList<CatalogCategory>>> FetchCategoriesAsync(
        string? bigCategoryId,
        CancellationToken cancellationToken)
    {
        var games = await FetchAllGamesAsync(cancellationToken).ConfigureAwait(false);
        if (games.IsFailure)
        {
            return Result<IReadOnlyList<CatalogCategory>>.Failure(games.Error);
        }

        var source = string.IsNullOrWhiteSpace(bigCategoryId)
            ? games.Value.Items
            : games.Value.Items.Where(game =>
                string.Equals(ReadId(game.GameId), bigCategoryId!.Trim(), StringComparison.Ordinal));

        var mapped = source
            .Select(game => new CatalogCategory(
                _options.ProviderUid,
                ReadId(game.GameId),
                ReadId(game.GameId),
                game.Name ?? string.Empty,
                BuildDescription(game),
                null,
                MapRegion(game.Region, game.Name ?? string.Empty)))
            .Where(item => item.ExternalId.Length > 0)
            .ToArray();

        return Result<IReadOnlyList<CatalogCategory>>.Success(mapped);
    }

    private async Task<Result<IReadOnlyList<CatalogItem>>> FetchItemsAsync(
        string? categoryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            // A full listing would need one HTTP call per game (357+ games).
            // For the unfiltered browse we expose only a small curated page; a
            // direct product URL uses GetItemByIdAsync which resolves the game first.
            var games = await FetchGamesPageAsync(null, cancellationToken).ConfigureAwait(false);
            if (games.IsFailure)
            {
                return Result<IReadOnlyList<CatalogItem>>.Failure(games.Error);
            }

            var all = new List<CatalogItem>();
            foreach (var game in games.Value.Items ?? [])
            {
                var gameId = ReadId(game.GameId);
                if (gameId.Length == 0) continue;

                var keys = await FetchKeysAsync(gameId, cancellationToken).ConfigureAwait(false);
                if (keys.IsFailure) continue;

                all.AddRange(keys.Value);
            }

            return Result<IReadOnlyList<CatalogItem>>.Success(all);
        }

        var keysResult = await FetchKeysAsync(categoryId.Trim(), cancellationToken).ConfigureAwait(false);
        return keysResult.IsFailure
            ? Result<IReadOnlyList<CatalogItem>>.Failure(keysResult.Error)
            : Result<IReadOnlyList<CatalogItem>>.Success(keysResult.Value);
    }

    private async Task<Result<ItemPriceQuote>> FetchItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var itemResult = await GetItemByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (itemResult.IsFailure)
        {
            return Result<ItemPriceQuote>.Failure(itemResult.Error);
        }

        var item = itemResult.Value;
        if (item.RetailPriceUsd is not { } retail)
        {
            return Result<ItemPriceQuote>.Failure(
                Error.PricesLocked("prices_locked", "Product prices are temporarily locked."));
        }

        return Result<ItemPriceQuote>.Success(new ItemPriceQuote(
            _options.ProviderUid,
            item.ExternalId,
            item.Name,
            retail,
            item.InStock,
            DateTimeOffset.UtcNow));
    }

    private async Task<Result<IReadOnlyList<CategoryRequirement>>> FetchRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<FazerCardsRegionRestrictionResponse>(
            string.Concat("gamekeys/region-restriction?game_id=", Uri.EscapeDataString(categoryId)),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            // Keys have no interactive player fields in v1. A failed restriction
            // lookup simply means "no extra info" rather than a broken catalog.
            return Result<IReadOnlyList<CategoryRequirement>>.Success([]);
        }

        var restriction = response.Value;
        var description = restriction.HasAvailability && restriction.Available is { Count: > 0 }
            ? string.Join(", ", restriction.Available
                .Where(country => !string.IsNullOrWhiteSpace(country.Code))
                .Select(country => country.Code!.Trim().ToUpperInvariant()))
            : string.Empty;

        if (description.Length == 0)
        {
            return Result<IReadOnlyList<CategoryRequirement>>.Success([]);
        }

        return Result<IReadOnlyList<CategoryRequirement>>.Success([
            new CategoryRequirement(
                "available_regions",
                RequirementDataType.Text,
                "Available regions",
                [description],
                null,
                null),
        ]);
    }

    private async Task<Result<bool>> FetchAccountHealthAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<FazerCardsAccountResponse>(MePath, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<bool>.Failure(response.Error);
        }

        return Result<bool>.Success(response.Value.Ok == true);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Result<FazerCardsGameCatalogPage>> FetchAllGamesAsync(CancellationToken cancellationToken)
    {
        var items = new List<FazerCardsGameDto>();
        string? cursor = null;

        for (var page = 0; page < MaxCursorPages; page++)
        {
            var path = string.Concat(
                GameKeysPath,
                "?limit=50",
                cursor is null ? string.Empty : string.Concat("&cursor=", Uri.EscapeDataString(cursor)));

            var response = await SendAsync<FazerCardsGameCatalogResponse>(path, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure)
            {
                return Result<FazerCardsGameCatalogPage>.Failure(response.Error);
            }

            if (response.Value.Items is { } batch)
            {
                items.AddRange(batch);
            }

            var meta = response.Value.Meta;
            if (meta is null || !meta.HasMore || string.IsNullOrWhiteSpace(meta.NextCursor))
            {
                break;
            }

            cursor = meta.NextCursor;
        }

        return Result<FazerCardsGameCatalogPage>.Success(new FazerCardsGameCatalogPage(items));
    }

    private async Task<Result<FazerCardsGameCatalogPage>> FetchGamesPageAsync(
        string? cursor,
        CancellationToken cancellationToken)
    {
        var path = string.Concat(
            GameKeysPath,
            "?limit=",
            UnfilteredGameLimit.ToString(CultureInfo.InvariantCulture),
            cursor is null ? string.Empty : string.Concat("&cursor=", Uri.EscapeDataString(cursor)));

        var response = await SendAsync<FazerCardsGameCatalogResponse>(path, cancellationToken).ConfigureAwait(false);
        return response.IsFailure
            ? Result<FazerCardsGameCatalogPage>.Failure(response.Error)
            : Result<FazerCardsGameCatalogPage>.Success(new FazerCardsGameCatalogPage(response.Value.Items ?? []));
    }

    private async Task<Result<IReadOnlyList<CatalogItem>>> FetchKeysAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        var path = string.Concat("gamekeys/keys?game_id=", Uri.EscapeDataString(gameId));
        var response = await SendAsync<FazerCardsKeysResponse>(path, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogItem>>.Failure(response.Error);
        }

        var payload = response.Value;
        var gameName = payload.GameName ?? string.Concat("Game ", gameId);
        var region = MapRegion(payload.Region, gameName);
        var variant = MapVariant(payload.Keys?.FirstOrDefault()?.Name);
        var regionLock = payload.RegionRestriction
            ? new RegionLockInfo("restricted", [], [])
            : new RegionLockInfo("free", [], []);

        var mapped = new List<CatalogItem>();
        foreach (var key in payload.Keys ?? [])
        {
            var keyId = ReadId(key.KeyId);
            if (keyId.Length == 0) continue;

            var effectiveVariant = MapVariant(key.Name);
            var externalId = string.Concat(gameId, "--", keyId);
            Money? retail = null;
            if (key.PriceUsd is { } cost && cost >= 0m)
            {
                retail = _pricingService.CalculateRetailPrice(cost, gameId);
            }

            mapped.Add(new CatalogItem(
                _options.ProviderUid,
                externalId,
                gameId,
                gameName,
                key.Name ?? gameName,
                variantLabel(effectiveVariant),
                null,
                ProductType.TopUp,
                region,
                retail,
                key.Stock > 0,
                retail is null,
                effectiveVariant,
                regionLock,
                payload.Platform));
        }

        return Result<IReadOnlyList<CatalogItem>>.Success(mapped);
    }

    private static string variantLabel(GameKeyVariant variant) => variant switch
    {
        GameKeyVariant.Dlc => "DLC / expansion",
        GameKeyVariant.SeasonPass => "Season pass / deluxe",
        _ => "Full game key",
    };

    private async Task<Result<T>> GetOrCreateAsync<T>(
        string key,
        int ttlSeconds,
        Func<CancellationToken, Task<Result<T>>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await _cache.GetOrCreateAsync(
                key,
                async token =>
                {
                    var result = await fetch(token).ConfigureAwait(false);
                    if (result.IsFailure) throw new CachedFailureException(result.Error);
                    return result.Value;
                },
                TimeSpan.FromSeconds(ttlSeconds),
                cancellationToken).ConfigureAwait(false);

            return Result<T>.Success(value);
        }
        catch (CachedFailureException exception)
        {
            return Result<T>.Failure(exception.Error);
        }
    }

    private async Task<Result<TResponse>> SendAsync<TResponse>(
        string relativePath,
        CancellationToken cancellationToken)
        where TResponse : FazerCardsResponseBase
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, relativePath));
        request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result<TResponse>.Failure(MapUpstreamError(
                    response.StatusCode,
                    TryReadError(body),
                    ReadRetryAfter(response),
                    relativePath));
            }

            TResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Game-key upstream returned an invalid payload for {Path}.", relativePath);
                return Result<TResponse>.Failure(UnavailableError());
            }

            if (payload is null || payload.Ok != true)
            {
                return Result<TResponse>.Failure(MapUpstreamError(
                    response.StatusCode,
                    payload is null ? null : new FazerCardsErrorDto { Error = payload.Error, Code = payload.Code },
                    ReadRetryAfter(response),
                    relativePath));
            }

            return Result<TResponse>.Success(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            _logger.LogWarning(exception, "Game-key upstream request to {Path} did not complete.", relativePath);
            return Result<TResponse>.Failure(UnavailableError());
        }
    }

    private Error MapUpstreamError(
        HttpStatusCode statusCode,
        FazerCardsErrorDto? upstream,
        int? retryAfter,
        string path)
    {
        _logger.LogWarning(
            "Game-key upstream request {Path} failed with HTTP {Status} and code {Code}.",
            path,
            (int)statusCode,
            upstream?.Code);

        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => Error.RateLimited(
                "provider_rate_limited", "Catalog service is busy. Please retry shortly.", retryAfter),
            HttpStatusCode.Conflict => Error.Conflict(
                "pricing_updating", "Product pricing is currently being refreshed.", retryAfter ?? 30),
            _ => UnavailableError(),
        };
    }

    private static Error UnavailableError() =>
        Error.ProviderUnavailable("catalog_provider_unavailable", "Catalog service is temporarily unavailable.");

    private static FazerCardsErrorDto? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonSerializer.Deserialize<FazerCardsErrorDto>(body, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return (int)Math.Ceiling(delta.TotalSeconds);
        if (retryAfter?.Date is { } date)
        {
            var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? (int)Math.Ceiling(seconds) : 0;
        }
        return null;
    }

    private RegionCode MapRegion(string? rawRegion, string fallbackName)
    {
        var region = _regionResolver.ResolveRegion(rawRegion ?? string.Empty, fallbackName);
        return region == RegionCode.OTHER && string.Equals(rawRegion?.Trim(), "GLOBAL", StringComparison.OrdinalIgnoreCase)
            ? RegionCode.GLOBAL
            : region;
    }

    private static GameKeyVariant MapVariant(string? name)
    {
        var value = name ?? string.Empty;
        if (value.Contains("season pass", StringComparison.OrdinalIgnoreCase)
            || value.Contains("deluxe", StringComparison.OrdinalIgnoreCase))
        {
            return GameKeyVariant.SeasonPass;
        }
        if (value.Contains("dlc", StringComparison.OrdinalIgnoreCase)
            || value.Contains("expansion", StringComparison.OrdinalIgnoreCase))
        {
            return GameKeyVariant.Dlc;
        }
        if (value.Length == 0) return GameKeyVariant.Unknown;
        return GameKeyVariant.BaseGame;
    }

    private static string BuildDescription(FazerCardsGameDto game) =>
        string.Join(" · ", new[]
        {
            game.Region is null ? string.Empty : string.Concat("Region: ", game.Region),
            game.Platform is null ? string.Empty : string.Concat("Platform: ", game.Platform),
            game.RegionRestriction ? "Region lock applies" : string.Empty,
        }.Where(part => part.Length > 0));

    private static (string? GameId, string? KeyId) SplitItemId(string itemId)
    {
        var separator = itemId.IndexOf("--", StringComparison.Ordinal);
        if (separator <= 0 || separator + 2 >= itemId.Length) return (null, null);
        return (itemId[..separator], itemId[(separator + 2)..]);
    }

    private static string ReadId(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeKeySegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "all";
        var trimmed = value.Trim();
        var length = Math.Min(trimmed.Length, 64);
        var builder = new System.Text.StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            var character = trimmed[index];
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }
        return builder.ToString();
    }

    private string CacheKey(string suffix) =>
        string.Concat("catalog:", _options.ProviderUid.ToString("D"), ":", suffix);

    private sealed record FazerCardsGameCatalogPage(IReadOnlyList<FazerCardsGameDto> Items);

    private sealed class CachedFailureException(Error error) : Exception(
        "The upstream result is a failure and must not be cached.")
    {
        public Error Error { get; } = error;
    }
}
