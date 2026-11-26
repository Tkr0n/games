using System.Globalization;
using VerkkuGames.Api.Extensions;
using VerkkuGames.Application.Abstractions;
using VerkkuGames.Application.Catalog.Queries;
using VerkkuGames.Application.Common;
using VerkkuGames.Domain.Catalog;
using VerkkuGames.Domain.ValueObjects;

namespace VerkkuGames.Api.Endpoints;

/// <summary>
/// Read-only catalog surface. Every endpoint is a thin shell: parse and validate
/// query values, delegate to the Application query handler, map failures once.
/// </summary>
public static class CatalogEndpoints
{
    private const string GeneralRateLimitPolicy = "general";
    private const string PriceRateLimitPolicy = "price";
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 24;

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/catalog")
            .RequireRateLimiting(GeneralRateLimitPolicy);

        group.MapGet("/big-categories", GetBigCategoriesAsync);
        group.MapGet("/categories", GetCategoriesAsync);
        group.MapGet("/items", GetItemsAsync);
        group.MapGet("/items/{providerUid:guid}/{itemId}", GetItemDetailAsync);
        group.MapGet("/items/{providerUid:guid}/{itemId}/price", GetItemPriceAsync)
            .RequireRateLimiting(PriceRateLimitPolicy);
        group.MapGet("/categories/{providerUid:guid}/{categoryId}/requirements", GetCategoryRequirementsAsync);

        return endpoints;
    }

    private static async Task<IResult> GetBigCategoriesAsync(
        HttpContext httpContext,
        string? providerUid,
        IQueryHandler<GetBigCategoriesQuery, IReadOnlyList<CatalogBigCategory>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseProviderUid(providerUid, out var parsedProviderUid))
        {
            return InvalidProviderUid(httpContext);
        }

        var result = await handler
            .HandleAsync(new GetBigCategoriesQuery(parsedProviderUid), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> GetCategoriesAsync(
        HttpContext httpContext,
        string? providerUid,
        string? bigCategoryId,
        string? region,
        string? includeGlobal,
        string? search,
        IQueryHandler<GetCategoriesQuery, IReadOnlyList<CatalogCategory>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseProviderUid(providerUid, out var parsedProviderUid))
        {
            return InvalidProviderUid(httpContext);
        }

        if (!TryParseRegion(region, out var parsedRegion))
        {
            return InvalidRegion(httpContext);
        }

        if (!TryParseBoolean(includeGlobal, out var includeGlobalValue))
        {
            return InvalidBoolean(httpContext, "includeGlobal");
        }

        var result = await handler
            .HandleAsync(
                new GetCategoriesQuery(
                    parsedProviderUid,
                    bigCategoryId,
                    parsedRegion,
                    includeGlobalValue,
                    search),
                cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> GetItemsAsync(
        HttpContext httpContext,
        string? providerUid,
        string? categoryId,
        string? region,
        string? includeGlobal,
        string? productType,
        string? inStockOnly,
        string? search,
        string? page,
        string? pageSize,
        string? platform,
        string? genre,
        string? yearFrom,
        string? yearTo,
        string? multiplayerOnly,
        IQueryHandler<GetCatalogItemsQuery, PagedResult<CatalogItem>> handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseProviderUid(providerUid, out var parsedProviderUid))
        {
            return InvalidProviderUid(httpContext);
        }

        if (!TryParseRegion(region, out var parsedRegion))
        {
            return InvalidRegion(httpContext);
        }

        if (!TryParseProductType(productType, out var parsedProductType))
        {
            return ResultExtensions.ValidationProblem(
                httpContext,
                "'productType' must be 'topup' or 'voucher'.");
        }

        if (!TryParseBoolean(includeGlobal, out var includeGlobalValue))
        {
            return InvalidBoolean(httpContext, "includeGlobal");
        }

        if (!TryParseBoolean(inStockOnly, out var inStockOnlyValue))
        {
            return InvalidBoolean(httpContext, "inStockOnly");
        }

        if (!TryParseInteger(page, DefaultPage, out var pageValue))
        {
            return ResultExtensions.ValidationProblem(
                httpContext,
                "'page' must be an integer greater than or equal to 1.");
        }

        if (!TryParseInteger(pageSize, DefaultPageSize, out var pageSizeValue))
        {
            return ResultExtensions.ValidationProblem(
                httpContext,
                "'pageSize' must be an integer between 1 and 100.");
        }

        if (!TryParseBoolean(multiplayerOnly, out var multiplayerOnlyValue))
        {
            return InvalidBoolean(httpContext, "multiplayerOnly");
        }

        if (!TryParseOptionalInteger(yearFrom, 1970, 2100, out var yearFromValue))
        {
            return ResultExtensions.ValidationProblem(
                httpContext,
                "'yearFrom' must be an integer between 1970 and 2100.");
        }

        if (!TryParseOptionalInteger(yearTo, 1970, 2100, out var yearToValue))
        {
            return ResultExtensions.ValidationProblem(
                httpContext,
                "'yearTo' must be an integer between 1970 and 2100.");
        }

        var result = await handler
            .HandleAsync(
                new GetCatalogItemsQuery(
                    parsedProviderUid,
                    categoryId,
                    parsedRegion,
                    includeGlobalValue,
                    parsedProductType,
                    inStockOnlyValue,
                    search,
                    pageValue,
                    pageSizeValue,
                    platform?.Trim(),
                    genre?.Trim(),
                    yearFromValue,
                    yearToValue,
                    multiplayerOnlyValue),
                cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> GetItemDetailAsync(
        Guid providerUid,
        string itemId,
        HttpContext httpContext,
        IQueryHandler<GetCatalogItemDetailQuery, CatalogItemDetail> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetCatalogItemDetailQuery(providerUid, itemId), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> GetItemPriceAsync(
        Guid providerUid,
        string itemId,
        HttpContext httpContext,
        IQueryHandler<GetItemPriceQuery, ItemPriceQuote> handler,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        var result = await handler
            .HandleAsync(new GetItemPriceQuery(providerUid, itemId), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> GetCategoryRequirementsAsync(
        Guid providerUid,
        string categoryId,
        HttpContext httpContext,
        IQueryHandler<GetCategoryRequirementsQuery, IReadOnlyList<CategoryRequirement>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetCategoryRequirementsQuery(providerUid, categoryId), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(httpContext);
    }

    private static bool TryParseProviderUid(string? raw, out Guid? providerUid)
    {
        providerUid = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!Guid.TryParse(raw.Trim(), out var parsed))
        {
            return false;
        }

        providerUid = parsed;
        return true;
    }

    private static bool TryParseRegion(string? raw, out RegionCode? region)
    {
        region = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var trimmed = raw.Trim();
        foreach (var name in Enum.GetNames<RegionCode>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                region = Enum.Parse<RegionCode>(name, ignoreCase: true);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseProductType(string? raw, out ProductType? productType)
    {
        productType = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var normalized = raw.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (string.Equals(normalized, "topup", StringComparison.OrdinalIgnoreCase))
        {
            productType = ProductType.TopUp;
            return true;
        }

        if (string.Equals(normalized, "voucher", StringComparison.OrdinalIgnoreCase))
        {
            productType = ProductType.Voucher;
            return true;
        }

        return false;
    }

    private static bool TryParseBoolean(string? raw, out bool value)
    {
        value = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return bool.TryParse(raw.Trim(), out value);
    }

    private static bool TryParseInteger(string? raw, int fallback, out int value)
    {
        value = fallback;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return int.TryParse(
            raw.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryParseOptionalInteger(
        string? raw,
        int min,
        int max,
        out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < min || parsed > max) return false;
        value = parsed;
        return true;
    }

    private static IResult InvalidProviderUid(HttpContext httpContext) =>
        ResultExtensions.ValidationProblem(
            httpContext,
            "'providerUid' must be a valid provider identifier.");

    private static IResult InvalidRegion(HttpContext httpContext) =>
        ResultExtensions.ValidationProblem(
            httpContext,
            "'region' must be one of: USA, GLOBAL, MX, PA, CO, OTHER.");

    private static IResult InvalidBoolean(HttpContext httpContext, string parameterName) =>
        ResultExtensions.ValidationProblem(
            httpContext,
            $"'{parameterName}' must be 'true' or 'false'.");
}
