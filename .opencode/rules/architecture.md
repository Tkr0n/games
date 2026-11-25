# Rule: Architecture (backend .NET 10)

Applies to `backend/**`. Read together with `docs/design/2026-11-25-verkku-games-v1-design.md`.

## 1. Dependency direction

```text
Api ---> Application ---> Domain
 |            ^
 +--> Infrastructure ----+
```

| Project | Allowed references | Forbidden |
| --- | --- | --- |
| `VerkkuGames.Domain` | none | NuGet packages, `HttpClient`, ASP.NET, EF, Redis, JSON attributes |
| `VerkkuGames.Application` | `Domain` | concrete `HttpClient`, Redis, Minimal APIs, provider SDKs |
| `VerkkuGames.Infrastructure` | `Application`, `Domain` | `Api` types |
| `VerkkuGames.Api` | `Application`, `Infrastructure` | business logic, direct provider calls |

**DO**

- Put business invariants in `Domain` (value objects, `Money`, `Result<T>`, `Error`).
- Put ports + use cases in `Application`; implementations in `Infrastructure`.
- Keep endpoints thin: validate input → call `IQueryHandler` → map to DTO / `ProblemDetails`.
- Use `IServiceCollection` extension methods per layer (`AddApplication()`, `AddInfrastructure(config)`).
- Keep `Program.cs` composition-only.

**DO NOT**

- Reference `Infrastructure` from `Application` or `Domain`.
- Add EF Core, MediatR, AutoMapper, Mapster, or any commercial-license package.
- Put `HttpClient`, `StackExchange.Redis`, or provider DTOs in `Application`.
- Create a static service locator or resolve services from `IServiceProvider` inside handlers.
- Share mutable static state between requests.

## 2. Money and numeric precision

**DO**

- Use `decimal` for money, rates, and percentages.
- Parse with `decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture)`.
- Format with `CultureInfo.InvariantCulture`.
- Round with `MidpointRounding.AwayFromZero`: `USD/USDT/USDC` → 2, `ETH` → 6, `BTC` → 8.
- Validate `Amount >= 0` in `Money`.

**DO NOT**

- Use `float`, `double`, `double.Parse`, or `Convert.ToDouble` for money/rates.
- Rely on the thread culture (the Pi or a dev machine may run `es-VE`).
- Use banker's rounding (`ToEven`) for retail prices.
- Add `Bs`/`VES`.

## 3. CQRS ligero (sin MediatR)

```csharp
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}
```

**DO**

- One handler per query, `sealed`, constructor-injected ports.
- Validate with a FluentValidation validator registered in DI; return `ErrorType.Validation` on failure.
- Return `Result<T>`; never throw for expected business failures.
- Keep queries immutable `record`s with explicit defaults.

**DO NOT**

- Add a dispatcher/pub-sub pipeline or behaviors.
- Inject `IServiceProvider` into handlers.
- Return provider DTOs from handlers; map to domain models.

## 4. Errors

`Error(string Code, string Message, ErrorType Type, int? RetryAfterSeconds = null)` with `ErrorType`: `Validation`, `NotFound`, `Unauthorized`, `Forbidden`, `RateLimited`, `Conflict`, `ProviderUnavailable`, `PricesLocked`.

**DO**

- Map `ErrorType` to HTTP once, in the API layer (`Validation`→400, `NotFound`→404, `Unauthorized`→401, `Forbidden`→403, `RateLimited`→429 + `Retry-After`, `Conflict`→409, `PricesLocked`→422, `ProviderUnavailable`→503).
- Return RFC 9457 `ProblemDetails` with a stable `code`, no stack traces.
- Log full provider detail **server-side only** with structured logging.

**DO NOT**

- Leak provider error messages, `unit_price`, tokens, or `InternalKey` in responses.
- Use exceptions for expected flows (`NotFound`, `PricesLocked`, rate limits).
- Return `200` with an ad-hoc `{ error: ... }` shape.

## 5. Provider abstraction (`ICatalogProvider`)

**DO**

- Expose `string InternalKey` (internal only) and `Guid ProviderUid` (public, opaque, stable).
- Resolve providers exclusively through `ICatalogProviderRegistry` by `Guid`.
- Keep all provider-specific quirks (envelopes, codes, `prices_locked`, field names, image inheritance) inside the adapter.
- Validate `ProviderUid` as `Guid` at the API boundary; unknown UID → `404`, never a provider name.
- Normalize provider data to domain models before it leaves `Infrastructure`.

**DO NOT**

- Branch on `InternalKey`/provider name in `Application` or `Api`.
- Accept raw provider IDs as `Guid` collisions or expose numeric provider IDs as identity.
- Return `unit_price`/`original_price` in any DTO.
- Add a new provider without adapter tests (success, locked prices, 401/403, 429, 404, malformed JSON, non-English culture).

## 6. Prices

- `MinimumMarginUsd` is a **floor**, not an additive fee.
- `effectivePercent == 0` (explicit override) ⇒ `retail = cost` (no floor).
- Otherwise `retail = max(cost * (1 + p/100), cost + MinimumMarginUsd)`, then round 2 dec away-from-zero.
- `prices_locked`/`RELOAD_REQUIRED` ⇒ listing keeps the item with `RetailPriceUsd = null`, `PricesLocked = true`; live price returns `PricesLocked`.
- Reseller cost never serialized.

## 7. Regions

- Resolve only from `categoryName` + optional `itemName`; never call `/requirements` in listings.
- Case-insensitive, **word-boundary** regex; `OTHER` fallback.
- `includeGlobal=true` with a country region adds `GLOBAL` products.
- `ExtractBaseTitle` strips region tokens/empty parentheses to link sibling categories.

## 8. Caching

- `HybridCache` (L1 + L2 Redis) through `ICacheService`; TTLs: big categories 30 m, categories 15 m, items 10 m, requirements 30 m, live price 60 s, crypto rates 60 s.
- Key prefix `verkkugames:v1:` from `Cache:KeyPrefix`.
- Keys contain `ProviderUid`, never `InternalKey`, never raw user input without normalization.
- Never cache auth failures or `ProblemDetails`.
- Unit tests must not require Redis.

## 9. HTTP resilience

- `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`.
- 8 s per attempt; retry with exponential backoff + jitter **only** on `5xx`, `408`, network errors; circuit breaker on.
- Never retry `400/401/403/404`.
- Propagate `CancellationToken` everywhere.

## 10. Configuration

- Options classes use DataAnnotations + `ValidateOnStart()`.
- Env prefix mapping: `Shop2TopUp__KeyId`, `ApiSecurity__ClientApiKey`, `Pricing__MinimumMarginUsd`, `Cache__RedisConnectionString`.
- Secrets only via User Secrets (dev) or env vars (prod). Never `appsettings.json` with real values.
- OpenAPI enabled in `Development` only.

## Pre-completion checklist

- [ ] `dotnet build backend/VerkkuGames.slnx` clean (warnings are errors).
- [ ] `dotnet test backend/VerkkuGames.slnx` green.
- [ ] New/changed behavior covered by unit or integration tests.
- [ ] No forbidden references added to `.csproj`.
- [ ] No `float`/`double` for money; no `Bs`/`VES`.
- [ ] No provider name/`unit_price` in public DTOs.
- [ ] TTLs and keys follow the table above.
