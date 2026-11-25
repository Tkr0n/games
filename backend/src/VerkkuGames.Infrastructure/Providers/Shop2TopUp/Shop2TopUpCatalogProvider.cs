using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.Common;
using VerkkuGames.Domain.ValueObjects;
using VerkkuGames.Infrastructure.Common;
using VerkkuGames.Infrastructure.Options;

namespace VerkkuGames.Infrastructure.Providers.Shop2TopUp;

public sealed class Shop2TopUpCatalogProvider : ICatalogProvider
{
    private const string BigCategoriesPath = "catalog/big-categories?for_ui=true";
    private const string CategoriesPath = "catalog/categories?for_ui=true";
    private const string ItemsPath = "catalog/subcategories";
    private const string AccountPath = "account";
    private const string NoRequirementsErrorCode = "no_requirements_found";
    private const int MaxCacheKeySegmentLength = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _httpClient;
    private readonly Shop2TopUpOptions _options;
    private readonly CacheOptions _cacheOptions;
    private readonly IPricingService _pricingService;
    private readonly IRegionResolver _regionResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<Shop2TopUpCatalogProvider> _logger;
    private readonly Uri _baseAddress;
    private readonly string _authorizationHeader;

    public Shop2TopUpCatalogProvider(
        HttpClient httpClient,
        IOptions<Shop2TopUpOptions> options,
        IOptions<CacheOptions> cacheOptions,
        IPricingService pricingService,
        IRegionResolver regionResolver,
        ICacheService cache,
        ILogger<Shop2TopUpCatalogProvider> logger)
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
        _authorizationHeader = string.Concat("Bearer ", _options.KeyId, ".", _options.KeySecret);
    }

    public string InternalKey => "shop2topup";

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

    public Task<Result<ItemPriceQuote>> GetItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return GetOrCreateAsync(
            CacheKey(string.Concat("price:", NormalizeKeySegment(itemId))),
            _cacheOptions.ItemPriceSeconds,
            token => FetchItemPriceAsync(itemId, token),
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        return GetOrCreateAsync(
            CacheKey(string.Concat("requirements:", NormalizeKeySegment(categoryId))),
            _cacheOptions.RequirementsSeconds,
            token => FetchCategoryRequirementsAsync(categoryId, token),
            cancellationToken);
    }

    public Task<Result<bool>> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            CacheKey("account"),
            _cacheOptions.AccountHealthSeconds,
            FetchAccountHealthAsync,
            cancellationToken);

    private async Task<Result<IReadOnlyList<CatalogBigCategory>>> FetchBigCategoriesAsync(
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<Shop2TopUpBigCategoriesResponse>(BigCategoriesPath, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogBigCategory>>.Failure(response.Error);
        }

        if (response.Value.BigCategories is not { } bigCategories)
        {
            LogInvalidPayload(BigCategoriesPath, null);
            return Result<IReadOnlyList<CatalogBigCategory>>.Failure(UnavailableError());
        }

        var mapped = new List<CatalogBigCategory>(bigCategories.Count);
        foreach (var dto in bigCategories)
        {
            var externalId = ReadId(dto.Id);
            if (externalId.Length == 0)
            {
                continue;
            }

            mapped.Add(new CatalogBigCategory(
                _options.ProviderUid,
                externalId,
                dto.Name ?? string.Empty,
                dto.Description ?? string.Empty,
                UrlSanitizer.SanitizeHttpsUrl(dto.Image)));
        }

        return Result<IReadOnlyList<CatalogBigCategory>>.Success(mapped);
    }

    private async Task<Result<IReadOnlyList<CatalogCategory>>> FetchCategoriesAsync(
        string? bigCategoryId,
        CancellationToken cancellationToken)
    {
        var path = CategoriesPath;
        if (!string.IsNullOrWhiteSpace(bigCategoryId))
        {
            path = string.Concat(path, "&bigCategoryId=", Uri.EscapeDataString(bigCategoryId.Trim()));
        }

        var response = await SendAsync<Shop2TopUpCategoriesResponse>(path, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogCategory>>.Failure(response.Error);
        }

        if (response.Value.Categories is not { } categories)
        {
            LogInvalidPayload(path, null);
            return Result<IReadOnlyList<CatalogCategory>>.Failure(UnavailableError());
        }

        var mapped = new List<CatalogCategory>(categories.Count);
        foreach (var dto in categories)
        {
            var externalId = ReadId(dto.Id);
            if (externalId.Length == 0)
            {
                continue;
            }

            var name = dto.Name ?? string.Empty;
            mapped.Add(new CatalogCategory(
                _options.ProviderUid,
                externalId,
                ReadId(dto.BigCategoryId),
                name,
                dto.Description ?? string.Empty,
                UrlSanitizer.SanitizeHttpsUrl(dto.Image),
                _regionResolver.ResolveRegion(name)));
        }

        return Result<IReadOnlyList<CatalogCategory>>.Success(mapped);
    }

    private async Task<Result<IReadOnlyList<CatalogItem>>> FetchItemsAsync(
        string? categoryId,
        CancellationToken cancellationToken)
    {
        var path = ItemsPath;
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            path = string.Concat(path, "?categoryId=", Uri.EscapeDataString(categoryId.Trim()));
        }

        var response = await SendAsync<Shop2TopUpSubcategoriesResponse>(path, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogItem>>.Failure(response.Error);
        }

        if (response.Value.Subcategories is not { } subcategories)
        {
            LogInvalidPayload(path, null);
            return Result<IReadOnlyList<CatalogItem>>.Failure(UnavailableError());
        }

        var categoryImages = await GetCategoryImageMapAsync(cancellationToken).ConfigureAwait(false);

        var mapped = new List<CatalogItem>(subcategories.Count);
        foreach (var dto in subcategories)
        {
            var externalId = ReadId(dto.ItemId ?? dto.Id);
            var parentCategoryId = ReadId(dto.CategoryId);
            if (externalId.Length == 0 || parentCategoryId.Length == 0)
            {
                continue;
            }

            var categoryName = dto.CategoryName ?? string.Empty;
            var name = dto.Name ?? string.Empty;

            var imageUrl = UrlSanitizer.SanitizeHttpsUrl(dto.Image);
            if (imageUrl is null && categoryImages.TryGetValue(parentCategoryId, out var inheritedImageUrl))
            {
                imageUrl = inheritedImageUrl;
            }

            var unitPrice = 0m;
            var pricesLocked = dto.PricesLocked == true || !TryParsePrice(dto.Price, out unitPrice);

            Money? retailPriceUsd = null;
            if (!pricesLocked)
            {
                retailPriceUsd = _pricingService.CalculateRetailPrice(unitPrice, parentCategoryId);
            }

            mapped.Add(new CatalogItem(
                _options.ProviderUid,
                externalId,
                parentCategoryId,
                categoryName,
                name,
                dto.Description ?? string.Empty,
                imageUrl,
                MapProductType(dto.ProductType),
                _regionResolver.ResolveRegion(categoryName, name),
                retailPriceUsd,
                dto.InStock ?? false,
                pricesLocked));
        }

        return Result<IReadOnlyList<CatalogItem>>.Success(mapped);
    }

    private async Task<Result<ItemPriceQuote>> FetchItemPriceAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        var path = string.Concat("catalog/subcategory/", Uri.EscapeDataString(itemId), "/price");
        var response = await SendAsync<Shop2TopUpPriceResponse>(path, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<ItemPriceQuote>.Failure(response.Error);
        }

        if (response.Value.Price is not { } price)
        {
            LogInvalidPayload(path, null);
            return Result<ItemPriceQuote>.Failure(UnavailableError());
        }

        if (!TryParsePrice(price.UnitPrice, out var unitPrice))
        {
            LogInvalidPayload(path, null);
            return Result<ItemPriceQuote>.Failure(UnavailableError());
        }

        var externalId = ReadId(price.ItemId);
        if (externalId.Length == 0)
        {
            externalId = itemId;
        }

        return Result<ItemPriceQuote>.Success(new ItemPriceQuote(
            _options.ProviderUid,
            externalId,
            price.ItemName ?? string.Empty,
            _pricingService.CalculateRetailPrice(unitPrice, categoryId: null),
            price.InStock ?? false,
            DateTimeOffset.UtcNow));
    }

    private async Task<Result<IReadOnlyList<CategoryRequirement>>> FetchCategoryRequirementsAsync(
        string categoryId,
        CancellationToken cancellationToken)
    {
        var path = string.Concat("catalog/category/", Uri.EscapeDataString(categoryId), "/requirements");
        var response = await SendAsync<Shop2TopUpRequirementsResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.Error.Code == NoRequirementsErrorCode
                ? Result<IReadOnlyList<CategoryRequirement>>.Success([])
                : Result<IReadOnlyList<CategoryRequirement>>.Failure(response.Error);
        }

        if (response.Value.Requirements is not { } requirements)
        {
            LogInvalidPayload(path, null);
            return Result<IReadOnlyList<CategoryRequirement>>.Failure(UnavailableError());
        }

        var mapped = new List<CategoryRequirement>(requirements.Count);
        foreach (var dto in requirements)
        {
            var fieldName = dto.FieldName?.Trim();
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                continue;
            }

            mapped.Add(new CategoryRequirement(
                fieldName,
                MapRequirementDataType(dto.DataType),
                dto.Placeholder,
                MapSelectOptions(dto.SelectOptions),
                dto.MinLength,
                dto.MaxLength));
        }

        return Result<IReadOnlyList<CategoryRequirement>>.Success(mapped);
    }

    private async Task<Result<bool>> FetchAccountHealthAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<Shop2TopUpAccountResponse>(AccountPath, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<bool>.Failure(response.Error);
        }

        if (response.Value.Account is not { } account)
        {
            LogInvalidPayload(AccountPath, null);
            return Result<bool>.Failure(UnavailableError());
        }

        return Result<bool>.Success(account.Enabled ?? false);
    }

    private async Task<Dictionary<string, string?>> GetCategoryImageMapAsync(CancellationToken cancellationToken)
    {
        var categoriesResult = await GetCategoriesAsync(bigCategoryId: null, cancellationToken).ConfigureAwait(false);
        if (categoriesResult.IsFailure)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var images = new Dictionary<string, string?>(categoriesResult.Value.Count, StringComparer.Ordinal);
        foreach (var category in categoriesResult.Value)
        {
            images[category.ExternalId] = category.ImageUrl;
        }

        return images;
    }

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
                    if (result.IsFailure)
                    {
                        // Failures must never be pinned in Redis: abort the cache factory instead.
                        throw new CachedFailureException(result.Error);
                    }

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
        where TResponse : Shop2TopUpResponseBase
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, relativePath));
        request.Headers.TryAddWithoutValidation("Authorization", _authorizationHeader);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var upstreamError = TryReadUpstreamError(body);
                return Result<TResponse>.Failure(MapUpstreamError(
                    response.StatusCode,
                    upstreamError,
                    ReadRetryAfterHeader(response),
                    relativePath));
            }

            TResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<TResponse>(body, SerializerOptions);
            }
            catch (JsonException exception)
            {
                LogInvalidPayload(relativePath, exception);
                return Result<TResponse>.Failure(UnavailableError());
            }

            if (payload is null)
            {
                LogInvalidPayload(relativePath, null);
                return Result<TResponse>.Failure(UnavailableError());
            }

            if (payload.Success == false)
            {
                return Result<TResponse>.Failure(MapUpstreamError(
                    response.StatusCode,
                    payload.Error,
                    ReadRetryAfterHeader(response),
                    relativePath));
            }

            return Result<TResponse>.Success(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            LogTransportFailure(relativePath, exception);
            return Result<TResponse>.Failure(UnavailableError());
        }
        catch (TimeoutRejectedException exception)
        {
            LogTransportFailure(relativePath, exception);
            return Result<TResponse>.Failure(UnavailableError());
        }
        catch (HttpRequestException exception)
        {
            LogTransportFailure(relativePath, exception);
            return Result<TResponse>.Failure(UnavailableError());
        }
    }

    private Error MapUpstreamError(
        HttpStatusCode statusCode,
        Shop2TopUpErrorDto? upstreamError,
        int? headerRetryAfterSeconds,
        string relativePath)
    {
        var errorCode = upstreamError?.Code?.Trim().ToUpperInvariant();
        var retryAfterSeconds = upstreamError?.RetryAfter ?? headerRetryAfterSeconds;

        if (errorCode == NoRequirementsErrorCode)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Catalog upstream reported no requirements for {Path}.", relativePath);
            }
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Catalog upstream request {Path} failed with HTTP {StatusCode} and error code {ErrorCode}.",
                    relativePath,
                    (int)statusCode,
                    errorCode);
            }
        }

        return errorCode switch
        {
            "RELOAD_REQUIRED" => Error.PricesLocked(
                "prices_locked",
                "Product prices are temporarily locked."),
            "NO_REQUIREMENTS_FOUND" => Error.NotFound(
                NoRequirementsErrorCode,
                "No requirements are defined for this category."),
            "ITEM_NOT_FOUND" or "INVALID_SUBCATEGORY" => Error.NotFound(
                "item_not_found",
                "The requested item was not found."),
            "RATE_LIMIT_EXCEEDED" => Error.RateLimited(
                "provider_rate_limited",
                "Catalog service is busy. Please retry shortly.",
                retryAfterSeconds),
            "PRICING_UPDATING" => Error.Conflict(
                "pricing_updating",
                "Product pricing is currently being refreshed. Please retry shortly.",
                retryAfterSeconds ?? 30),
            _ => MapStatusCodeFallback(statusCode, retryAfterSeconds),
        };
    }

    private static Error MapStatusCodeFallback(HttpStatusCode statusCode, int? retryAfterSeconds) =>
        statusCode switch
        {
            HttpStatusCode.TooManyRequests => Error.RateLimited(
                "provider_rate_limited",
                "Catalog service is busy. Please retry shortly.",
                retryAfterSeconds),
            HttpStatusCode.Conflict => Error.Conflict(
                "pricing_updating",
                "Product pricing is currently being refreshed. Please retry shortly.",
                retryAfterSeconds ?? 30),
            _ => UnavailableError(),
        };

    private void LogInvalidPayload(string relativePath, Exception? exception) =>
        _logger.LogWarning(exception, "Catalog upstream returned an invalid payload for {Path}.", relativePath);

    private void LogTransportFailure(string relativePath, Exception exception) =>
        _logger.LogWarning(exception, "Catalog upstream request to {Path} did not complete.", relativePath);

    private string CacheKey(string suffix) =>
        string.Concat("catalog:", _options.ProviderUid.ToString("D"), ":", suffix);

    private static Error UnavailableError() =>
        Error.ProviderUnavailable("catalog_provider_unavailable", "Catalog service is temporarily unavailable.");

    private static Shop2TopUpErrorDto? TryReadUpstreamError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Shop2TopUpResponseBase>(body, SerializerOptions)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadRetryAfterHeader(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return (int)Math.Ceiling(delta.TotalSeconds);
        }

        if (retryAfter.Date is { } date)
        {
            var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? (int)Math.Ceiling(seconds) : 0;
        }

        return null;
    }

    private static string ReadId(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeKeySegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "all";
        }

        var trimmed = value.Trim();
        var length = Math.Min(trimmed.Length, MaxCacheKeySegmentLength);
        var builder = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            var character = trimmed[index];
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }

        return builder.ToString();
    }

    private static bool TryParsePrice(string? rawPrice, out decimal price)
    {
        price = 0m;

        if (string.IsNullOrWhiteSpace(rawPrice))
        {
            return false;
        }

        return decimal.TryParse(
                rawPrice.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out price)
            && price >= 0m;
    }

    private static ProductType MapProductType(string? rawProductType) =>
        string.Equals(rawProductType?.Trim(), "voucher", StringComparison.OrdinalIgnoreCase)
            ? ProductType.Voucher
            : ProductType.TopUp;

    private static RequirementDataType MapRequirementDataType(string? rawDataType)
    {
        var value = rawDataType?.Trim();

        if (string.Equals(value, "number", StringComparison.OrdinalIgnoreCase))
        {
            return RequirementDataType.Number;
        }

        if (string.Equals(value, "single_select", StringComparison.OrdinalIgnoreCase))
        {
            return RequirementDataType.SingleSelect;
        }

        if (string.Equals(value, "multi_select", StringComparison.OrdinalIgnoreCase))
        {
            return RequirementDataType.MultiSelect;
        }

        return RequirementDataType.Text;
    }

    private static List<string> MapSelectOptions(List<JsonElement>? selectOptions)
    {
        if (selectOptions is not { Count: > 0 })
        {
            return [];
        }

        var values = new List<string>(selectOptions.Count);
        foreach (var option in selectOptions)
        {
            var value = option.ValueKind switch
            {
                JsonValueKind.String => option.GetString(),
                JsonValueKind.Number => option.GetRawText(),
                JsonValueKind.Object when option.TryGetProperty("value", out var inner) =>
                    inner.ValueKind == JsonValueKind.String ? inner.GetString() : inner.GetRawText(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private sealed class CachedFailureException(Error error) : Exception(
        "The upstream result is a failure and must not be cached.")
    {
        public Error Error { get; } = error;
    }
}
