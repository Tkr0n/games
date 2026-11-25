using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using VerkkuGames.Api.Contracts;
using VerkkuGames.Api.Endpoints;
using VerkkuGames.Api.Extensions;
using VerkkuGames.Api.Middleware;
using VerkkuGames.Api.Options;
using VerkkuGames.Application;
using VerkkuGames.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// HybridCache round-trips every entry through bytes. Use the public JSON
// contract so immutable domain values (Money) survive the cache intact.
builder.Services
    .AddHybridCache()
    .AddSerializerFactory<ApiJsonHybridCacheSerializerFactory>();

builder.Services
    .AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.ConfigureHttpJsonOptions(
    options => ApiJsonSerialization.Configure(options.SerializerOptions));

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;

        problem.Extensions["traceId"] = ResultExtensions.GetTraceId(context.HttpContext);

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = problem.Status == StatusCodes.Status500InternalServerError
                ? "internal_error"
                : "request_failed";
        }

        problem.Title = ResultExtensions.Sanitize(problem.Title);
        problem.Detail = ResultExtensions.Sanitize(problem.Detail);
    };
});

builder.Services.AddCors();
builder.Services
    .AddOptions<CorsOptions>()
    .Configure<IOptions<ApiSecurityOptions>>((cors, securityOptions) =>
    {
        var allowedOrigins = securityOptions.Value.AllowedOrigins;

        cors.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins);
            }
            else
            {
                // Production is Worker-to-API only: no browser cross-origin caller is allowed.
                policy.SetIsOriginAllowed(_ => false);
            }

            policy
                .WithMethods("GET", "OPTIONS")
                .WithHeaders("X-Api-Client-Key", "Content-Type");
        });
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (rejectedContext, cancellationToken) =>
    {
        var httpContext = rejectedContext.HttpContext;

        var retryAfterSeconds = rejectedContext.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var retryAfter)
                ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                : 60;

        httpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = ResultExtensions.CreateProblem(
            StatusCodes.Status429TooManyRequests,
            "Too Many Requests",
            "Too many requests. Please retry later.",
            "rate_limited",
            ResultExtensions.GetTraceId(httpContext),
            retryAfterSeconds);

        await Results.Problem(problem).ExecuteAsync(httpContext).ConfigureAwait(false);
    };
});

builder.Services
    .AddOptions<RateLimiterOptions>()
    .Configure<IOptions<ApiSecurityOptions>>((rateLimiter, securityOptions) =>
    {
        var security = securityOptions.Value;

        rateLimiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            _ => RateLimitPartition.GetFixedWindowLimiter(
                "global",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = security.GlobalPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        rateLimiter.AddPolicy("general", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ApiClientKeyMiddleware.ResolvePartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = security.GeneralPermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        rateLimiter.AddPolicy("price", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ApiClientKeyMiddleware.ResolvePartitionKey(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = security.PricePermitLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var app = builder.Build();

// Strict order: errors, headers, CORS, BFF auth, rate limiting, endpoints.
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();
app.UseMiddleware<ApiClientKeyMiddleware>();
app.UseRateLimiter();

app.MapCatalogEndpoints();
app.MapRatesEndpoints();
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
