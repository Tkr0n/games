using System.Net;
using System.Text.Json;
using Xunit;

namespace VerkkuGames.IntegrationTests;

public sealed class ApiSecurityAndRateLimitTests
{
    [Fact]
    public async Task MissingApiKey_WithSpoofedForwardedIp_Returns401ProblemDetails()
    {
        using var factory = new VerkkuGamesApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/rates");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Client-Ip", "1.2.3.4");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal("unauthorized", root.GetProperty("code").GetString());
        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("detail", out var detail) && detail.GetString()?.Contains("1.2.3.4", StringComparison.Ordinal) == true);
        Assert.Equal(0, factory.RatesHandler.RequestCount);
        Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidApiKey_Returns401ProblemDetails()
    {
        using var factory = new VerkkuGamesApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/rates");
        request.Headers.TryAddWithoutValidation("X-Api-Client-Key", "wrong-key-but-long-enough-000");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("unauthorized", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(0, factory.RatesHandler.RequestCount);
    }

    [Fact]
    public async Task HealthLive_WithoutApiKey_Returns200WithSecurityHeadersAndNoServerHeader()
    {
        using var factory = new VerkkuGamesApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        Assert.False(response.Headers.Contains("Server"));

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_ReportsReadyWithoutLeakingProviderDetails()
    {
        using var factory = new VerkkuGamesApiFactory();
        factory.CatalogHandler.Responder = _ => StubUpstreamResponses.Json(
            HttpStatusCode.OK,
            StubUpstreamResponses.AccountJson);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("reseller@example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_WhenProviderFails_Returns503DegradedWithoutLeakingUpstreamError()
    {
        using var factory = new VerkkuGamesApiFactory();
        factory.CatalogHandler.Responder = _ => StubUpstreamResponses.Error(
            HttpStatusCode.Forbidden,
            "ACCOUNT_FROZEN",
            "account frozen with wallet 1234.56");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("degraded", document.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("ACCOUNT_FROZEN", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubUpstreamResponses.ProviderName, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PriceRateLimit_IsPartitionedPerForwardedVisitorIp()
    {
        using var factory = new VerkkuGamesApiFactory
        {
            PricePermitLimitPerMinute = 3,
            GeneralPermitLimitPerMinute = 1000,
            GlobalPermitLimitPerMinute = 1000,
        };
        factory.CatalogHandler.Responder = StubUpstreamResponses.HappyCatalogResponse;
        factory.RatesHandler.Responder = StubUpstreamResponses.HappyRatesResponse;
        using var client = factory.CreateAuthenticatedClient();

        var pricePath =
            $"/api/v1/catalog/items/{VerkkuGamesApiFactory.ProviderUid:D}/999/price";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await SendPriceRequestAsync(client, pricePath, "203.0.113.10");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await SendPriceRequestAsync(client, pricePath, "203.0.113.10");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.True(limited.Headers.RetryAfter!.Delta >= TimeSpan.FromSeconds(1));

        var limitedBody = await limited.Content.ReadAsStringAsync();
        using var limitedDocument = JsonDocument.Parse(limitedBody);
        Assert.Equal("rate_limited", limitedDocument.RootElement.GetProperty("code").GetString());
        Assert.True(limitedDocument.RootElement.GetProperty("retryAfterSeconds").GetInt32() >= 1);

        using var otherVisitor = await SendPriceRequestAsync(client, pricePath, "203.0.113.20");
        Assert.Equal(HttpStatusCode.OK, otherVisitor.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendPriceRequestAsync(
        HttpClient client,
        string path,
        string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Client-Ip", forwardedFor);

        return await client.SendAsync(request).ConfigureAwait(false);
    }
}
