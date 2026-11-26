using System.Net;
using System.Text.Json;
using Xunit;

namespace VerkkuGames.IntegrationTests;

public sealed class CatalogEndpointsTests
{
    [Fact]
    public async Task HappyPath_AllEndpoints_ReturnFrontendCompatibleJsonWithoutProviderLeaks()
    {
        using var factory = new VerkkuGamesApiFactory();
        factory.CatalogHandler.Responder = StubUpstreamResponses.HappyCatalogResponse;
        factory.RatesHandler.Responder = StubUpstreamResponses.HappyRatesResponse;
        using var client = factory.CreateAuthenticatedClient();

        var providerUid = VerkkuGamesApiFactory.ProviderUid.ToString("D");
        var bodies = new List<string>();

        // ---------------------------------------------------- big categories
        var bigCategoriesBody = await GetOkBodyAsync(
            client,
            $"/api/v1/catalog/big-categories?providerUid={providerUid}",
            bodies);
        using (var document = JsonDocument.Parse(bigCategoriesBody))
        {
            var first = document.RootElement[0];
            Assert.Equal(VerkkuGamesApiFactory.ProviderUid, Guid.Parse(first.GetProperty("providerUid").GetString()!));
            Assert.Equal("1", first.GetProperty("externalId").GetString());
            Assert.Equal("Game Cards", first.GetProperty("name").GetString());
            Assert.Equal("https://cdn.test/big.png", first.GetProperty("imageUrl").GetString());
        }

        // -------------------------------------------------------- categories
        var categoriesBody = await GetOkBodyAsync(
            client,
            $"/api/v1/catalog/categories?providerUid={providerUid}&bigCategoryId=1&region=usa&includeGlobal=true",
            bodies);
        using (var document = JsonDocument.Parse(categoriesBody))
        {
            var first = document.RootElement[0];
            Assert.Equal("12", first.GetProperty("externalId").GetString());
            Assert.Equal("1", first.GetProperty("bigCategoryId").GetString());
            Assert.Equal("USA", first.GetProperty("region").GetString());
        }

        // ------------------------------------------------------------- items
        var itemsBody = await GetOkBodyAsync(
            client,
            $"/api/v1/catalog/items?providerUid={providerUid}&categoryId=12&region=USA&includeGlobal=true&page=1&pageSize=24",
            bodies);
        using (var document = JsonDocument.Parse(itemsBody))
        {
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("page").GetInt32());
            Assert.Equal(24, root.GetProperty("pageSize").GetInt32());
            Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, root.GetProperty("totalPages").GetInt32());
            Assert.Equal(2, root.GetProperty("items").GetArrayLength());

            var paid = root.GetProperty("items")[0];
            Assert.Equal("topup", paid.GetProperty("productType").GetString());
            Assert.Equal("USA", paid.GetProperty("region").GetString());
            Assert.True(paid.GetProperty("inStock").GetBoolean());
            Assert.False(paid.GetProperty("pricesLocked").GetBoolean());

            var retailPrice = paid.GetProperty("retailPriceUsd");
            Assert.Equal("USD", retailPrice.GetProperty("currency").GetString());
            // cost 0.95 + 0.25 floor => 1.20; the reseller cost must never surface.
            Assert.Equal(1.20m, retailPrice.GetProperty("amount").GetDecimal());

            var locked = root.GetProperty("items")[1];
            Assert.Equal("voucher", locked.GetProperty("productType").GetString());
            Assert.True(locked.GetProperty("pricesLocked").GetBoolean());
            Assert.Equal(JsonValueKind.Null, locked.GetProperty("retailPriceUsd").ValueKind);
        }

        // ------------------------------------------------------- item detail
        var detailBody = await GetOkBodyAsync(
            client,
            $"/api/v1/catalog/items/{providerUid}/999",
            bodies);
        using (var document = JsonDocument.Parse(detailBody))
        {
            var root = document.RootElement;
            Assert.Equal("999", root.GetProperty("item").GetProperty("externalId").GetString());

            var requirements = root.GetProperty("requirements");
            Assert.Equal(2, requirements.GetArrayLength());
            Assert.Equal("player_id", requirements[0].GetProperty("fieldName").GetString());
            Assert.Equal("text", requirements[0].GetProperty("dataType").GetString());
            Assert.Equal("zone_id", requirements[1].GetProperty("fieldName").GetString());

            var siblings = root.GetProperty("siblingItems");
            Assert.Equal(1, siblings.GetArrayLength());
            Assert.Equal("1000", siblings[0].GetProperty("externalId").GetString());

            Assert.Equal(0, root.GetProperty("relatedRegionCategories").GetArrayLength());
        }

        // -------------------------------------------------------- item price
        using (var priceResponse = await client.GetAsync(
            $"/api/v1/catalog/items/{providerUid}/999/price"))
        {
            Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);
            Assert.Contains("no-store", priceResponse.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

            var body = await priceResponse.Content.ReadAsStringAsync();
            bodies.Add(body);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Assert.Equal(providerUid, root.GetProperty("providerUid").GetString());
            Assert.Equal("999", root.GetProperty("externalItemId").GetString());
            Assert.Equal("PlayStation 10 USD", root.GetProperty("itemName").GetString());
            Assert.Equal(1.20m, root.GetProperty("retailPriceUsd").GetProperty("amount").GetDecimal());
            Assert.True(root.GetProperty("inStock").GetBoolean());
            Assert.True(DateTimeOffset.TryParse(
                root.GetProperty("quotedAtUtc").GetString(),
                out _));
        }

        // --------------------------------------------------- requirements
        var requirementsBody = await GetOkBodyAsync(
            client,
            $"/api/v1/catalog/categories/{providerUid}/12/requirements",
            bodies);
        using (var document = JsonDocument.Parse(requirementsBody))
        {
            Assert.Equal(2, document.RootElement.GetArrayLength());
            Assert.Equal("player_id", document.RootElement[0].GetProperty("fieldName").GetString());
            Assert.Equal("text", document.RootElement[0].GetProperty("dataType").GetString());
            Assert.Equal("zone_id", document.RootElement[1].GetProperty("fieldName").GetString());
        }

        // ------------------------------------------------------------ rates
        var ratesBody = await GetOkBodyAsync(client, "/api/v1/rates", bodies);
        using (var document = JsonDocument.Parse(ratesBody))
        {
            var root = document.RootElement;
            Assert.Equal("USD", root.GetProperty("baseCurrency").GetString());

            var rates = root.GetProperty("ratesPerUsd");
            Assert.Equal(1m, rates.GetProperty("USD").GetDecimal());
            Assert.Equal(1m, rates.GetProperty("USDT").GetDecimal());

            var bitcoinRate = rates.GetProperty("BTC").GetDecimal();
            Assert.True(bitcoinRate > 0m && bitcoinRate < 1m);

            var ethereumRate = rates.GetProperty("ETH").GetDecimal();
            Assert.True(ethereumRate > 0m && ethereumRate < 1m);

            Assert.True(DateTimeOffset.TryParse(root.GetProperty("retrievedAtUtc").GetString(), out _));
        }

        // -------------------------------------------------- global leak gate
        foreach (var body in bodies)
        {
            Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(StubUpstreamResponses.ResellerCostField, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("original_price", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ItemPrice_WhenUpstreamRequiresReload_Returns422PricesLockedWithoutProviderLeak()
    {
        using var factory = new VerkkuGamesApiFactory();
        factory.CatalogHandler.Responder = _ => StubUpstreamResponses.Error(
            HttpStatusCode.Forbidden,
            "RELOAD_REQUIRED",
            "Contact billing to reload the account balance.");
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/v1/catalog/items/{VerkkuGamesApiFactory.ProviderUid:D}/999/price");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(422, root.GetProperty("status").GetInt32());
        Assert.Equal("prices_locked", root.GetProperty("code").GetString());
        Assert.Equal("Prices Locked", root.GetProperty("title").GetString());
        Assert.DoesNotContain("RELOAD_REQUIRED", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("billing", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ACCOUNT_FROZEN", 403)]
    [InlineData("IP_NOT_ALLOWED", 401)]
    [InlineData("INVALID_API_KEY", 401)]
    public async Task Catalog_WhenUpstreamCredentialError_Returns503SanitizedProblemDetails(
        string upstreamCode,
        int upstreamStatus)
    {
        using var factory = new VerkkuGamesApiFactory();
        factory.CatalogHandler.Responder = _ => StubUpstreamResponses.Error(
            (HttpStatusCode)upstreamStatus,
            upstreamCode,
            "account frozen with wallet 1234.56 and token sk_live_do_not_leak");
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/catalog/big-categories");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("catalog_provider_unavailable", root.GetProperty("code").GetString());
        Assert.Equal("Service Unavailable", root.GetProperty("title").GetString());
        Assert.DoesNotContain(upstreamCode, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frozen", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk_live", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-key-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/v1/catalog/items?page=0")]
    [InlineData("/api/v1/catalog/items?pageSize=500")]
    [InlineData("/api/v1/catalog/items?region=MARS")]
    [InlineData("/api/v1/catalog/items?productType=bad")]
    [InlineData("/api/v1/catalog/categories?region=MARS")]
    public async Task InvalidQueryParameters_Return400ValidationProblemDetails(string path)
    {
        using var factory = new VerkkuGamesApiFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("validation_failed", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(0, factory.CatalogHandler.RequestCount);
    }

    private static async Task<string> GetOkBodyAsync(
        HttpClient client,
        string path,
        List<string> bodies)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        bodies.Add(body);
        return body;
    }
}
