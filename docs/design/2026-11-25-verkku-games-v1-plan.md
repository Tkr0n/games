# Verkku Games v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Heavy coding tasks are designed to run in parallel waves using `opencode/deepseek-v4.1-flash` subagents under orchestrator coordination.

**Goal:** Build the lasting, secure v1 foundation of **Verkku Games**: a Clean Architecture .NET 10 catalog & pricing API (SHOP2TOPUP + Binance crypto rates + Redis/Valkey `HybridCache`, Raspberry Pi ARM64 ready) and a bilingual (ES/EN) SEO-first Astro 5 + React 19 (TypeScript, Bun) storefront targeting Cloudflare Workers with Static Assets, complete with repo rules, READMEs, and agent profiles.

**Architecture:** Monorepo at `F:\Verkku\local\verkku-games\` split into `backend/` (.NET 10 Clean Architecture: `Domain` → `Application` → `Infrastructure` → `Api` + Unit/Integration tests), `frontend/` (Astro 5 SSR on Cloudflare Workers acting as a BFF with `X-Api-Client-Key` + `X-Forwarded-Client-Ip` toward the Raspberry Pi API, plus React islands for currency, region, filters, and product detail), and governance docs (`AGENTS.md`, `.opencode/rules/*`, `.opencode/agents/*`, READMEs). Wholesale provider identities are strictly hidden behind an opaque `Guid` (`ProviderUid`) across all public APIs, URLs, HTML, and `sitemap.xml`.

**Tech Stack:** .NET 10 (`10.0.x`), ASP.NET Core Minimal APIs, `Microsoft.Extensions.Caching.Hybrid`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Extensions.Http.Resilience`, `FluentValidation` (Apache-2.0, zero commercial-license packages), `xUnit`, `NSubstitute`, `Valkey/Redis` (Docker), **Bun `1.4.5`**, **Astro 5** (`@astrojs/cloudflare`, `@astrojs/react`), **React 19**, **TypeScript** (strict), **Tailwind CSS v4** (`@tailwindcss/vite`).

**Spec:** `F:\Verkku\local\docs\superpowers\specs\2026-11-25-verkku-games-v1-design.md`

---

## Global Constraints

- Monorepo root: `F:\Verkku\local\verkku-games\` (initialized as its own git repository).
- **Zero runtime mocks or fake catalog fallbacks:** Runtime always queries real SHOP2TOPUP and Binance (via Redis HybridCache). Mocks exist exclusively in automated test projects.
- **Opaque Provider UID:** `"shop2topup"` must NEVER appear in public API JSON, public URLs, HTML, OpenGraph, JSON-LD, `robots.txt`, or `sitemap.xml`. Every public provider reference uses `Guid ProviderUid` (default `d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42`).
- **Money & Rates Precision:** Never use `float` or `double` for money or exchange rates in .NET; always use `decimal` with `CultureInfo.InvariantCulture`. Currencies supported: `USD` (base reference, 2 dec), `USDT` (2 dec), `USDC` (2 dec), `ETH` (6 dec), `BTC` (8 dec). No Bs/VES anywhere.
- **No reseller cost leakage:** SHOP2TOPUP `unit_price` / `original_price` and raw provider error messages or credentials must never be returned by public API endpoints.
- **No commercial-license packages:** Do NOT install MediatR or AutoMapper. Use lightweight custom `IQueryHandler<TQuery, TResult>` and explicit manual mapping.
- **Brand & Logo:** Official logo copied from `C:\Users\Saydenier\Pictures\logoverkkuformatos\SVG\verkku-logo-notagline.svg` and stored strictly as `frontend/public/assets/logo.svg`. Official Verkku dark palette (`#05342E`, `#06352F`, `#0A453D`, `#1A8154`, `#209959`, `#25C877`, `#81CB79`, `#8BCCAB`, `#2E6D6D`, `#FBF7F5`), fonts `Bricolage Grotesque` + `Poppins`, natural gamer voice in native ES (`/es/...`) and EN (`/en/...`).
- **v1 Product CTA:** Read-only showcase / *"Próximamente"* (*"Coming soon"*) — no cart, checkout, order creation, or WhatsApp/Telegram links.
- **Supply-chain & Runtime Security:** Upgrade Bun to `1.4.5` before installing frontend packages; enforce `minimumReleaseAge = 604800` in `bunfig.toml`, lockfile strictness, minimal `trustedDependencies`, and verify `bun audit` + `dotnet list package --vulnerable --include-transitive`. Protect `/api/v1/*` with `X-Api-Client-Key` (`CryptographicOperations.FixedTimeEquals`) and per-visitor rate limiting via post-auth `X-Forwarded-Client-Ip`.
- **Target Deployment Artifacts:** Frontend targets **Cloudflare Workers with Static Assets** (`wrangler.jsonc` with `main` + `assets`); Backend includes ARM64/x64 multi-stage `Dockerfile` (non-root) and `docker-compose.yml` (`valkey/valkey:8-alpine`, `maxmemory 128mb`, `allkeys-lru`).

---

## Review Focus

1. **SHOP2TOPUP `prices_locked: true` & `RELOAD_REQUIRED` (403):** When a reseller account has locked prices, `/catalog/subcategories` omits `price` and sets `prices_locked: true`, and `/price` returns 403 `RELOAD_REQUIRED`. Deserialization must succeed with `RetailPriceUsd = null, PricesLocked = true` in listings and return a controlled `ErrorType.PricesLocked` (HTTP 422/503 mapped cleanly) on live price queries. *(Pinned in Task 6 & Task 8)*
2. **Untrusted `X-Forwarded-Client-Ip` spoofing & shared-bucket rate limiting:** Unauthenticated callers with spoofed `X-Forwarded-Client-Ip` must fail with `401` before reaching rate limiting or providers; authenticated BFF requests with distinct `X-Forwarded-Client-Ip` values must get independent rate-limit buckets so one abusive visitor gets `429` without blocking others. *(Pinned in Task 8)*
3. **Unsafe external image URLs from provider metadata:** Non-`https://` image values (`javascript:`, `http://`, `data:`, malformed URIs) from SHOP2TOPUP must be rejected to `null` in backend normalization and fall back to `/assets/placeholder-product.svg` in frontend rendering. *(Pinned in Task 4 & Task 6)*
4. **Wholesale provider name leakage (`"shop2topup"`) on error paths and SEO outputs:** Upstream auth/freeze/IP errors (`ACCOUNT_FROZEN`, `IP_NOT_ALLOWED`, `INVALID_API_KEY`) must map to sanitized `ProblemDetails` with zero mention of `"shop2topup"`, raw `unit_price`, or auth tokens; frontend pages and `sitemap.xml` must never contain `"shop2topup"`. *(Pinned in Task 7 & Task 8)*
5. **Non-English OS locale decimal parsing & small-value BTC/ETH formatting:** Parsing `"0.950000"` or `"96420.15000000"` under `es-VE` / `es-ES` thread cultures must always succeed via `CultureInfo.InvariantCulture`, and formatting small USD amounts (`$0.50`) into BTC/ETH must render fixed-point `0.00000519` (never scientific notation `5.19e-6` or `0.00`). *(Pinned in Task 2, Task 4 & Task 6)*

---

## File Structure & Parallel Execution Waves

- **Wave 0 (Sequential — Orchestrator):** Task 1 (Bun upgrade, repo bootstrap, logo copy, .NET 10 solution & Central Package Management scaffold, Astro/Bun hardened scaffold, Docker Compose & `.env.example` files).
- **Wave 1 (Parallel Subagents — `opencode/deepseek-v4.1-flash`):**
  - Task 2: `backend/src/VerkkuGames.Domain/*` + `backend/tests/VerkkuGames.UnitTests/Domain/*`
  - Task 3: Governance docs (`README.md`, `backend/README.md`, `frontend/README.md`, `AGENTS.md`, `.opencode/rules/*`, `.opencode/agents/*`)
  - Task 4: Frontend foundation (`global.css`, `favicon.svg`, `placeholder-product.svg`, `site.webmanifest`, `middleware.ts`, `i18n/*`, `lib/{types,currency,regions,seo}.ts`, Bun unit tests)
- **Wave 2 (Parallel Subagents — `opencode/deepseek-v4.1-flash`):**
  - Task 5: `backend/src/VerkkuGames.Application/*` + `backend/tests/VerkkuGames.UnitTests/Application/*`
  - Task 6: `backend/src/VerkkuGames.Infrastructure/*` + `backend/tests/VerkkuGames.UnitTests/Infrastructure/*`
  - Task 7: Frontend server API client, BFF `/api/*`, `robots.txt.ts`, `sitemap.xml.ts`, `index.astro`, `404.astro` + Bun tests
- **Wave 3 (Parallel Subagents — `opencode/deepseek-v4.1-flash`):**
  - Task 8: `backend/src/VerkkuGames.Api/*`, `backend/Dockerfile`, `backend/tests/VerkkuGames.IntegrationTests/*`
  - Task 9: Frontend `BaseLayout.astro`, Astro components, React islands, all `/[lang]/*` bilingual pages + typecheck/build/tests
- **Wave 4 (Final Verification — Orchestrator):** Full test suites, security audits (`bun audit`, `dotnet list package --vulnerable --include-transitive`), leak grep check (`shop2topup`), and whole-branch review.

---

### Task 1: Workspace Bootstrap, Bun Upgrade & Hardened Project Scaffolds

**Files:**
- Create: `F:\Verkku\local\verkku-games\.gitignore`
- Create: `F:\Verkku\local\verkku-games\.env.example`
- Create: `F:\Verkku\local\verkku-games\docker-compose.yml`
- Create: `F:\Verkku\local\verkku-games\docs\design\2026-11-25-verkku-games-v1-design.md` (copy of approved spec)
- Create: `F:\Verkku\local\verkku-games\backend\Directory.Build.props`
- Create: `F:\Verkku\local\verkku-games\backend\Directory.Packages.props`
- Create: `F:\Verkku\local\verkku-games\backend\VerkkuGames.slnx`
- Create: `F:\Verkku\local\verkku-games\backend\.env.example`
- Create: `F:\Verkku\local\verkku-games\backend\src\VerkkuGames.{Domain,Application,Infrastructure,Api}\*.csproj`
- Create: `F:\Verkku\local\verkku-games\backend\tests\VerkkuGames.{UnitTests,IntegrationTests}\*.csproj`
- Create: `F:\Verkku\local\verkku-games\frontend\bunfig.toml`
- Create: `F:\Verkku\local\verkku-games\frontend\package.json`
- Create: `F:\Verkku\local\verkku-games\frontend\tsconfig.json`
- Create: `F:\Verkku\local\verkku-games\frontend\astro.config.mjs`
- Create: `F:\Verkku\local\verkku-games\frontend\wrangler.jsonc`
- Create: `F:\Verkku\local\verkku-games\frontend\.env.example`
- Create: `F:\Verkku\local\verkku-games\frontend\public\assets\logo.svg` (copied from `C:\Users\Saydenier\Pictures\logoverkkuformatos\SVG\verkku-logo-notagline.svg`)

**Interfaces:**
- Produces: Buildable empty `.NET 10` solution (`net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, Central Package Management) with strict Clean Architecture project references (`Api -> Application, Infrastructure`, `Infrastructure -> Application, Domain`, `Application -> Domain`, `Domain -> none`), and installable Bun `1.4.5` Astro 5 + React 19 + Tailwind v4 + `@astrojs/cloudflare` frontend workspace.

- [ ] **Step 1: Upgrade Bun to latest (`1.4.5`) and verify version**

Run: `bun upgrade && bun --version`
Expected: `1.4.5` (or latest 1.4.x).

- [ ] **Step 2: Initialize monorepo directory, git repo, `.gitignore`, logo asset, and spec copy**

Create `F:\Verkku\local\verkku-games\`, run `git init`, copy `C:\Users\Saydenier\Pictures\logoverkkuformatos\SVG\verkku-logo-notagline.svg` to `frontend\public\assets\logo.svg`, copy `F:\Verkku\local\docs\superpowers\specs\2026-11-25-verkku-games-v1-design.md` to `docs\design\2026-11-25-verkku-games-v1-design.md`, and write `.gitignore` (excluding `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, `.astro/`, `.wrangler/`, `*.user`).

- [ ] **Step 3: Scaffold `.NET 10` Clean Architecture solution with Central Package Management**

Create `Directory.Build.props` (`TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`), `Directory.Packages.props` (`ManagePackageVersionsCentrally=true` pinning `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`, `Microsoft.Extensions.Caching.Hybrid`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `NSubstitute`, `Microsoft.AspNetCore.Mvc.Testing`), `VerkkuGames.slnx`, and the 6 `.csproj` files with exact dependency direction.

- [ ] **Step 4: Scaffold hardened `frontend/` workspace with Bun, Astro 5, React 19, Tailwind v4, and Cloudflare Workers config**

Write `frontend/bunfig.toml`:
```toml
[install]
minimumReleaseAge = 604800
frozenLockfile = false
```
Write `frontend/package.json` (scripts: `dev`, `build`, `preview`, `check`, `test`, `audit`; minimal trusted deps: `astro`, `@astrojs/cloudflare`, `@astrojs/react`, `react`, `react-dom`, `tailwindcss`, `@tailwindcss/vite`, `@types/react`, `@types/react-dom`, `typescript`, `wrangler`), `tsconfig.json` (`astro/tsconfigs/strict`, React JSX), `astro.config.mjs` (`output: 'server'`, `adapter: cloudflare()`, `integrations: [react()]`, `vite: { plugins: [tailwindcss()] }`, `i18n: { locales: ['es', 'en'], defaultLocale: 'es', routing: { prefixDefaultLocale: true } }`), and `wrangler.jsonc` configured for Cloudflare Workers with Static Assets (`main: "./dist/_worker.js/index.js"`, `assets: { directory: "./dist", binding: "ASSETS" }`, `compatibility_flags: ["nodejs_compat"]`). Install with `bun install`.

- [ ] **Step 5: Create `docker-compose.yml` and `.env.example` templates**

Write `docker-compose.yml` with `valkey` (`valkey/valkey:8-alpine`, `maxmemory 128mb`, `maxmemory-policy allkeys-lru`, `127.0.0.1:6379:6379` for dev only, healthcheck `valkey-cli ping`, plus commented/profiled `api` service for ARM64 RPi). Write root, `backend/`, and `frontend/` `.env.example` files documenting every env var without real secrets.

- [ ] **Step 6: Verify both scaffolds build cleanly and pass initial vulnerability checks**

Run:
- `dotnet restore backend/VerkkuGames.slnx && dotnet build backend/VerkkuGames.slnx && dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive`
- `bun audit` (in `frontend/`)
Expected: Zero build errors, zero vulnerable packages reported.

- [ ] **Step 7: Commit bootstrap scaffold**

Run: `git add -A && git commit -m "chore: bootstrap Verkku Games v1 monorepo scaffold"`

---

### Task 2: Backend Domain Layer (`VerkkuGames.Domain`) + Unit Tests *(Wave 1 — Parallel)*

**Files:**
- Create: `backend/src/VerkkuGames.Domain/Common/ErrorType.cs`
- Create: `backend/src/VerkkuGames.Domain/Common/Error.cs`
- Create: `backend/src/VerkkuGames.Domain/Common/Result.cs`
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/CurrencyCode.cs`
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/RegionCode.cs`
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/ProductType.cs`
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/RequirementDataType.cs`
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/Money.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/CatalogBigCategory.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/CatalogCategory.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/CatalogItem.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/CatalogItemDetail.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/CategoryRequirement.cs`
- Create: `backend/src/VerkkuGames.Domain/Catalog/ItemPriceQuote.cs`
- Create: `backend/src/VerkkuGames.Domain/Pricing/ExchangeRatesSnapshot.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Domain/MoneyTests.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Domain/ResultTests.cs`

**Interfaces:**
- Consumes: .NET 10 BCL only (zero third-party dependencies).
- Produces:
  - `enum ErrorType { Validation, NotFound, Unauthorized, Forbidden, RateLimited, Conflict, ProviderUnavailable, PricesLocked }`
  - `sealed record Error(string Code, string Message, ErrorType Type, int? RetryAfterSeconds = null)` with factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`, `Error.RateLimited(...)`, `Error.Conflict(...)`, `Error.ProviderUnavailable(...)`, `Error.PricesLocked(...)`)
  - `sealed class Result<T>` (`bool IsSuccess`, `bool IsFailure`, `T Value`, `Error Error`, `Result<T>.Success(T)`, `Result<T>.Failure(Error)`, `Match<TOut>(...)`)
  - `enum CurrencyCode { USD, USDT, USDC, BTC, ETH }`
  - `enum RegionCode { USA, GLOBAL, MX, PA, CO, OTHER }`
  - `enum ProductType { TopUp, Voucher }`
  - `enum RequirementDataType { Text, Number, SingleSelect, MultiSelect }`
  - `readonly record struct Money(decimal Amount, CurrencyCode Currency)` with `Money.Create(decimal amount, CurrencyCode currency)`, `Money.ParseInvariant(string rawAmount, CurrencyCode currency)`, `Money.GetDecimalPlaces(CurrencyCode currency)` (`USD/USDT/USDC => 2`, `ETH => 6`, `BTC => 8`), `MidpointRounding.AwayFromZero` rounding, `ConvertTo(CurrencyCode target, decimal ratePerUsd)` (throws/guards negative amounts and non-positive rates).
  - Immutable records:
    - `CatalogBigCategory(Guid ProviderUid, string ExternalId, string Name, string Description, string? ImageUrl)`
    - `CatalogCategory(Guid ProviderUid, string ExternalId, string BigCategoryId, string Name, string Description, string? ImageUrl, RegionCode Region)`
    - `CatalogItem(Guid ProviderUid, string ExternalId, string CategoryId, string CategoryName, string Name, string Description, string? ImageUrl, ProductType ProductType, RegionCode Region, Money? RetailPriceUsd, bool InStock, bool PricesLocked)`
    - `CatalogItemDetail(CatalogItem Item, IReadOnlyList<CategoryRequirement> Requirements, IReadOnlyList<CatalogItem> SiblingItems, IReadOnlyList<CatalogCategory> RelatedRegionCategories)`
    - `CategoryRequirement(string FieldName, RequirementDataType DataType, string? Placeholder, IReadOnlyList<string> SelectOptions, int? MinLength, int? MaxLength)`
    - `ItemPriceQuote(Guid ProviderUid, string ExternalItemId, string ItemName, Money RetailPriceUsd, bool InStock, DateTimeOffset QuotedAtUtc)`
    - `ExchangeRatesSnapshot(CurrencyCode BaseCurrency, IReadOnlyDictionary<CurrencyCode, decimal> RatesPerUsd, DateTimeOffset RetrievedAtUtc)`

- [ ] **Step 1: Write failing unit tests for `Money` and `Result<T>`**

In `MoneyTests.cs`, pin:
- `Create_NegativeAmount_ThrowsArgumentOutOfRangeException`
- `Create_RoundsAwayFromZero_ByCurrencyPrecision` (`USD` `1.005m -> 1.01m`, `ETH` 6 dec, `BTC` 8 dec)
- `ParseInvariant_UnderSpanishLocale_ParsesDotDecimalString` (sets `CultureInfo.CurrentCulture = new CultureInfo("es-VE")`, parses `"0.950000"` as `USD` -> `0.95m`, pins Review Focus #5)
- `ConvertTo_SmallUsdToBtc_PreservesEightDecimalsWithoutZeroing` (`$0.50` at `ratePerUsd = 1m / 96420.15m` -> `0.00000519m` in `BTC`)
In `ResultTests.cs`, pin `Success_ExposesValue`, `Failure_AccessingValueThrowsInvalidOperationException`, `Failure_ExposesErrorWithRetryAfter`.

- [ ] **Step 2: Run domain tests to verify they fail**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Domain"`
Expected: Compilation/test failure because Domain types do not exist yet.

- [ ] **Step 3: Implement all Domain types in `backend/src/VerkkuGames.Domain/`**

Implement `ErrorType`, `Error`, `Result<T>`, enums, `Money`, and all 7 catalog/pricing records with strict nullability and zero external package references.

- [ ] **Step 4: Run domain tests to verify they pass**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Domain"`
Expected: All Domain tests PASS, zero compiler warnings.

---

### Task 3: Project Governance, READMEs, Rules & Agent Profiles *(Wave 1 — Parallel)*

**Files:**
- Create: `README.md`
- Create: `backend/README.md`
- Create: `frontend/README.md`
- Create: `AGENTS.md`
- Create: `.opencode/rules/architecture.md`
- Create: `.opencode/rules/security.md`
- Create: `.opencode/rules/frontend-seo-i18n.md`
- Create: `.opencode/agents/backend-architect.md`
- Create: `.opencode/agents/frontend-astro.md`
- Create: `.opencode/agents/security-auditor.md`

**Interfaces:**
- Consumes: Spec `docs/design/2026-11-25-verkku-games-v1-design.md` and scaffold paths from Task 1.
- Produces: Complete developer onboarding docs and AI-agent operating contracts matching the exact repo structure, commands, security policies, and architectural rules.

- [ ] **Step 1: Write `README.md`, `backend/README.md`, and `frontend/README.md`**

Cover:
- Root `README.md`: Verkku Games architecture diagram (Cloudflare Workers Astro SSR BFF → Cloudflare Tunnel → Raspberry Pi .NET 10 API + Valkey/Redis → SHOP2TOPUP & Binance), prerequisites (`.NET 10`, `Bun 1.4.5+`, Docker), local quickstart, env vars overview, verification & security audit commands, and v2 roadmap.
- `backend/README.md`: Clean Architecture layer boundaries, how to add a new `ICatalogProvider` with an opaque `ProviderUid`, `PricingOptions` floor math, SHOP2TOPUP/Binance/Redis setup, User Secrets, running tests, and ARM64 Raspberry Pi Docker/native build notes.
- `frontend/README.md`: Astro 5 SSR + React 19 islands + Tailwind v4 + Bun hardening (`bunfig.toml`), Verkku brand tokens & `/assets/logo.svg`, ES/EN i18n workflow, SEO files (`robots.txt`, `sitemap.xml`, JSON-LD, `hreflang`), Cloudflare Workers deployment (`wrangler.jsonc` secrets).

- [ ] **Step 2: Write `AGENTS.md` and `.opencode/rules/*.md`**

- `AGENTS.md`: Root instructions referencing `.opencode/rules/*.md`, mandatory verification commands (`dotnet test`, `dotnet list package --vulnerable`, `bun test`, `bun run check`, `bun run build`, `bun audit`), explicit bans (no runtime catalog mocks, no exposing wholesale provider names or `unit_price`, no `float`/`double` for money, no MediatR/AutoMapper).
- `.opencode/rules/architecture.md`: Dependency flow rules, CQRS handler conventions, `Result<T>` error handling, provider abstraction contract.
- `.opencode/rules/security.md`: Supply-chain policy (`minimumReleaseAge`, `--frozen-lockfile`, NuGet CPM & vulnerability audit), secret handling, constant-time API key comparison, `X-Forwarded-Client-Ip` trust boundary, CSP & security headers, HTTPS-only external media validation.
- `.opencode/rules/frontend-seo-i18n.md`: Island discipline (Astro static HTML by default, React only for interactive controls), bilingual ES/EN parity checks, canonical/`hreflang`/JSON-LD rules, WCAG AA contrast on Verkku palette.

- [ ] **Step 3: Write `.opencode/agents/*.md`**

Create focused project agent profiles (`backend-architect.md`, `frontend-astro.md`, `security-auditor.md`) with clear role, scope, allowed directories, required rules, and pre-completion verification checklists.

- [ ] **Step 4: Verify all documented paths and commands match the scaffold**

Inspect every file created in Task 3 to ensure zero broken relative links, zero leaked secrets, and 100% alignment with `VerkkuGames.slnx`, `frontend/package.json`, and `docker-compose.yml`.

---

### Task 4: Frontend Foundation (Brand Styles, Assets, Security Middleware, i18n, Currency/Regions/SEO Utils) + Tests *(Wave 1 — Parallel)*

**Files:**
- Create: `frontend/public/favicon.svg`
- Create: `frontend/public/assets/placeholder-product.svg`
- Create: `frontend/public/site.webmanifest`
- Create: `frontend/src/styles/global.css`
- Create: `frontend/src/middleware.ts`
- Create: `frontend/src/lib/types.ts`
- Create: `frontend/src/lib/currency.ts`
- Create: `frontend/src/lib/regions.ts`
- Create: `frontend/src/lib/seo.ts`
- Create: `frontend/src/i18n/config.ts`
- Create: `frontend/src/i18n/es.ts`
- Create: `frontend/src/i18n/en.ts`
- Create: `frontend/src/i18n/utils.ts`
- Test: `frontend/src/lib/__tests__/currency.test.ts`
- Test: `frontend/src/lib/__tests__/seo_and_media_utils.test.ts`
- Test: `frontend/src/i18n/__tests__/i18n.test.ts`

**Interfaces:**
- Consumes: `frontend/public/assets/logo.svg` (from Task 1), Tailwind v4, Astro middleware API.
- Produces:
  - Types in `src/lib/types.ts` matching Backend API v1 JSON contracts: `CurrencyCode` (`'USD'|'USDT'|'USDC'|'BTC'|'ETH'`), `RegionCode` (`'USA'|'GLOBAL'|'MX'|'PA'|'CO'|'OTHER'`), `ProductType` (`'topup'|'voucher'`), `MoneyDto`, `BigCategoryDto`, `CategoryDto`, `CatalogItemDto`, `CategoryRequirementDto`, `CatalogItemDetailDto`, `ItemPriceQuoteDto`, `ExchangeRatesDto`, `PagedResponse<T>`, `ProblemDetailsDto`.
  - `src/lib/currency.ts`: `SUPPORTED_CURRENCIES`, `CURRENCY_DECIMALS`, `convertUsdAmount(usdAmount: number, currency: CurrencyCode, rates: ExchangeRatesDto | null): string | null`, `formatMoneyDisplay(usdAmount: number, currency: CurrencyCode, rates: ExchangeRatesDto | null, locale: Locale): { primary: string; referenceUsd: string; unavailableRate: boolean }`.
  - `src/lib/regions.ts`: `PRIMARY_REGIONS` (`['USA','GLOBAL','MX','PA','CO']`), `ALL_REGIONS`, `getRegionMeta(code: RegionCode, locale: Locale)`, `isValidRegionCode(val: string): val is RegionCode`.
  - `src/lib/seo.ts`: `sanitizeExternalImageUrl(url?: string | null): string` (returns `/assets/placeholder-product.svg` unless valid `https:` URL), `buildCanonicalUrl(siteUrl: string, pathname: string): string`, `buildHreflangLinks(siteUrl: string, pathname: string)`, `buildOrganizationJsonLd(siteUrl: string)`, `buildWebSiteJsonLd(siteUrl: string, locale: Locale)`, `buildBreadcrumbJsonLd(...)`, `buildProductJsonLd(siteUrl: string, locale: Locale, detail: CatalogItemDetailDto)`.
  - `src/i18n/config.ts` & `utils.ts`: `Locale = 'es' | 'en'`, `DEFAULT_LOCALE = 'es'`, `isLocale(v)`, `detectPreferredLocale(cookieHeader, acceptLanguageHeader)`, `getDictionary(locale)`, `switchLocalePath(pathname, targetLocale)`.
  - `src/middleware.ts`: Sets `Content-Security-Policy`, `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`.

- [ ] **Step 1: Write failing Bun tests for currency, SEO/media sanitization, and i18n**

Pin:
- `currency.test.ts`: converting `$0.50` to `BTC` at `1 / 96420.15` renders fixed-point `"0.00000519 BTC"` with zero scientific notation (Review Focus #5); `USD` renders 2 decimals; `ETH` renders 6 decimals; missing rates gracefully keeps USD reference and flags `unavailableRate: true`.
- `seo_and_media_utils.test.ts`: `sanitizeExternalImageUrl` rejects `javascript:alert(1)`, `http://example.com/a.png`, `data:image/svg+xml,...`, `""`, `null` -> returns `"/assets/placeholder-product.svg"` (Review Focus #3); accepts `https://cdn.example.com/game.webp`; `buildProductJsonLd` emits valid `@type: "Product"` + `Offer` (`InStock` / `OutOfStock`) with zero occurrences of `"shop2topup"`.
- `i18n.test.ts`: `es.ts` and `en.ts` have identical key sets; `detectPreferredLocale` defaults to `'es'`, respects `'en-US'` and cookie override; `switchLocalePath('/es/catalog', 'en') === '/en/catalog'`.

- [ ] **Step 2: Run Bun tests to verify they fail**

Run: `bun test` (in `frontend/`)
Expected: FAIL because modules do not exist yet.

- [ ] **Step 3: Implement brand CSS, SVG assets, `site.webmanifest`, `middleware.ts`, `i18n/*`, and `lib/*`**

- `global.css`: `@import "tailwindcss";` + `@theme` custom properties matching Verkku palette (`--color-verkku-dark: #05342E`, `#06352F`, `#0A453D`, `#1A8154`, `#209959`, `#25C877`, `#81CB79`, `#8BCCAB`, `#2E6D6D`, `#FBF7F5`, `#EFF0E9`) + font families `Bricolage Grotesque` and `Poppins`.
- `es.ts` & `en.ts`: Natural gamer copy (authentic, crisp, transparent, zero corporate stiffness).
- `currency.ts`: Uses fixed-point decimal formatting (`toFixed(decimals)` / `Intl.NumberFormat` without scientific notation).
- `middleware.ts`: Applies strict security headers (allowing `img-src 'self' https: data:` for safe HTTPS provider product thumbnails and Google Fonts if used, blocking inline script abuse).

- [ ] **Step 4: Run Bun tests to verify they pass**

Run: `bun test` (in `frontend/`)
Expected: All frontend foundation tests PASS.

---

### Task 5: Backend Application Layer (`VerkkuGames.Application`) + Unit Tests *(Wave 2 — Parallel)*

**Files:**
- Create: `backend/src/VerkkuGames.Application/Abstractions/IQueryHandler.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/ICatalogProvider.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/ICatalogProviderRegistry.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/IExchangeRateProvider.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/ICacheService.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/IPricingService.cs`
- Create: `backend/src/VerkkuGames.Application/Abstractions/IRegionResolver.cs`
- Create: `backend/src/VerkkuGames.Application/Options/PricingOptions.cs`
- Create: `backend/src/VerkkuGames.Application/Services/PricingService.cs`
- Create: `backend/src/VerkkuGames.Application/Services/RegionResolver.cs`
- Create: `backend/src/VerkkuGames.Application/Services/CatalogProviderRegistry.cs`
- Create: `backend/src/VerkkuGames.Application/Common/PagedResult.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetBigCategoriesQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetCategoriesQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetCatalogItemsQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetCatalogItemDetailQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetItemPriceQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Catalog/Queries/GetCategoryRequirementsQuery.cs`
- Create: `backend/src/VerkkuGames.Application/Rates/Queries/GetExchangeRatesQuery.cs`
- Create: `backend/src/VerkkuGames.Application/DependencyInjection.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Application/PricingServiceTests.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Application/RegionResolverTests.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Application/CatalogQueriesTests.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Application/QueryValidatorsTests.cs`

**Interfaces:**
- Consumes: `VerkkuGames.Domain` (Task 2).
- Produces:
  - `ICatalogProvider`: `string InternalKey`, `Guid ProviderUid`, `GetBigCategoriesAsync(ct)`, `GetCategoriesAsync(string? bigCategoryId, ct)`, `GetItemsAsync(string? categoryId, ct)`, `GetItemPriceAsync(string itemId, ct)`, `GetCategoryRequirementsAsync(string categoryId, ct)`, `CheckHealthAsync(ct)` — all returning `Task<Result<...>>`.
  - `ICatalogProviderRegistry`: `Result<ICatalogProvider> GetByUid(Guid providerUid)`, `Result<ICatalogProvider> GetDefault()`, `IReadOnlyList<ICatalogProvider> GetAll()`.
  - `IExchangeRateProvider`: `Task<Result<ExchangeRatesSnapshot>> GetCryptoRatesAsync(CancellationToken ct)`.
  - `ICacheService`: `Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, TimeSpan ttl, CancellationToken ct)`.
  - `IPricingService`: `Money CalculateRetailPrice(decimal providerUnitCostUsd, string? categoryId = null)`.
  - `IRegionResolver`: `RegionCode ResolveRegion(string categoryName, string? itemName = null)`, `string ExtractBaseTitle(string categoryName)`.
  - `IQueryHandler<TQuery, TResult>`: `Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct)`.
  - `PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)`.
  - `AddApplication(this IServiceCollection services)` registering services, handlers, and FluentValidation validators.

- [ ] **Step 1: Write failing unit tests for Pricing, RegionResolver, Validators, and Query Handlers**

Pin exact spec behaviors:
- `PricingServiceTests.cs`:
  - Standard markup (`cost = 10.00m`, `DefaultMarkupPercent = 10m`, `MinimumMarginUsd = 0.25m` -> `11.00m`).
  - Floor enforcement (`cost = 1.00m`, `DefaultMarkupPercent = 10m`, `MinimumMarginUsd = 0.25m` -> `max(1.10, 1.25) = 1.25m`).
  - Explicit `0%` category override (`CategoryMarkupOverrides["12"] = 0m`, `cost = 1.00m` -> `1.00m`, no floor forced).
  - Positive category override (`CategoryMarkupOverrides["12"] = 20m`, `cost = 10.00m` -> `12.00m`).
  - Rounding `AwayFromZero` to 2 decimals.
- `RegionResolverTests.cs`:
  - Matches `USA`, `GLOBAL`, `MX`, `PA`, `CO`, and falls back to `OTHER` (word-boundary safe so `"Disco"` doesn't match `CO` and `"Spain"` doesn't match `PA`).
  - `ExtractBaseTitle("PlayStation Store Gift Card USA")` equals `ExtractBaseTitle("PlayStation Store Gift Card Mexico")` (normalized `"playstation store gift card"`).
- `CatalogQueriesTests.cs`:
  - `GetCatalogItemsQueryHandler` filters by `Region` exact (`IncludeGlobal = false`) vs inclusive (`IncludeGlobal = true` returns `USA` + `GLOBAL`), filters by `ProductType`, `InStockOnly`, `Search`, and paginates accurately.
  - `GetCatalogItemDetailQueryHandler` returns `CatalogItemDetail` with item, category requirements, sibling items in same category, and cross-region matching categories via `ExtractBaseTitle`, and returns `NotFound` when item doesn't exist.
  - Unknown `ProviderUid` returns `ErrorType.NotFound` without mentioning provider internal keys.
- `QueryValidatorsTests.cs`:
  - Rejects `Page < 1`, `PageSize < 1` or `> 100`, `Search.Length > 100`, invalid `ItemId` / `CategoryId` (empty or non-alphanumeric injection chars).

- [ ] **Step 2: Run Application tests to verify they fail**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Application"`
Expected: FAIL.

- [ ] **Step 3: Implement Application ports, options, services, queries, validators, and DI**

Implement all files listed above. Note: `GetCatalogItemsQuery` / `GetCategoriesQuery` validate via `IValidator<TQuery>` inside each handler (returning `Result<T>.Failure(Error.Validation(...))`), keeping API endpoints thin and uniform.

- [ ] **Step 4: Run Application tests to verify they pass**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Application"`
Expected: All Application tests PASS.

---

### Task 6: Backend Infrastructure Layer (`VerkkuGames.Infrastructure`) + Unit Tests *(Wave 2 — Parallel)*

**Files:**
- Create: `backend/src/VerkkuGames.Infrastructure/Options/Shop2TopUpOptions.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Options/BinanceOptions.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Options/CacheOptions.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Caching/RedisHybridCacheService.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/Shop2TopUp/Shop2TopUpDtos.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/Shop2TopUp/Shop2TopUpCatalogProvider.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/Binance/BinanceDtos.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/Binance/BinanceExchangeRateProvider.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Common/UrlSanitizer.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Infrastructure/Shop2TopUpCatalogProviderTests.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Infrastructure/BinanceExchangeRateProviderTests.cs`

**Interfaces:**
- Consumes: `VerkkuGames.Domain` (Task 2), `VerkkuGames.Application` (Task 5).
- Produces:
  - `Shop2TopUpOptions` (`BaseUrl` default `"https://shop2topup.com/api/endpoints/v1"`, `KeyId`, `KeySecret`, `ProviderUid` default `Guid.Parse("d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42")`, `TimeoutSeconds` default `8`).
  - `BinanceOptions` (`BaseUrl` default `"https://data-api.binance.vision"`, `TimeoutSeconds` default `5`).
  - `CacheOptions` (`RedisConnectionString`, `KeyPrefix` default `"verkkugames:v1:"`, TTL seconds: `BigCategoriesSeconds = 1800`, `CategoriesSeconds = 900`, `ItemsSeconds = 600`, `RequirementsSeconds = 1800`, `ItemPriceSeconds = 60`, `ExchangeRatesSeconds = 60`).
  - `Shop2TopUpCatalogProvider : ICatalogProvider` (uses `IPricingService`, `IRegionResolver`, `ICacheService`, `IHttpClientFactory`/typed `HttpClient`, `ILogger`).
  - `BinanceExchangeRateProvider : IExchangeRateProvider` (uses `ICacheService`, typed `HttpClient`, `ILogger`).
  - `RedisHybridCacheService : ICacheService` (wraps .NET 10 `HybridCache`).
  - `AddInfrastructure(this IServiceCollection services, IConfiguration configuration)` wiring options (`ValidateDataAnnotations().ValidateOnStart()`), Redis/HybridCache, typed HttpClients with `AddStandardResilienceHandler` (retry only transient `5xx`/`408`/network, never `400/401/403/404`), and providers.

- [ ] **Step 1: Write failing Infrastructure unit tests with stubbed `HttpMessageHandler`**

Pin:
- `Shop2TopUpCatalogProviderTests.cs`:
  - Sends exact `Authorization: Bearer {KeyId}.{KeySecret}` header and calls `/catalog/big-categories?for_ui=true`, `/catalog/categories?for_ui=true`, `/catalog/subcategories`, `/catalog/subcategory/{itemId}/price`, `/catalog/category/{categoryId}/requirements`.
  - Applies `IPricingService` markup so `RetailPriceUsd` reflects retail price, never raw `unit_price`.
  - Inherits `ImageUrl` from parent `CatalogCategory` when subcategory has no image; strips unsafe non-`https://` image URLs (`javascript:`, `http://`) to `null` (Review Focus #3).
  - Handles `prices_locked: true` (omitted `price` field) in `/catalog/subcategories` -> `RetailPriceUsd = null, PricesLocked = true` (Review Focus #1).
  - Maps `RELOAD_REQUIRED` (403) on `/price` -> `ErrorType.PricesLocked` (Review Focus #1).
  - Maps `NO_REQUIREMENTS_FOUND` (404) -> `Result.Success([])` (empty list).
  - Maps `ITEM_NOT_FOUND` (404) -> `ErrorType.NotFound`.
  - Maps `RATE_LIMIT_EXCEEDED` (429) -> `ErrorType.RateLimited` with `RetryAfterSeconds`.
  - Maps `PRICING_UPDATING` (409) -> `ErrorType.Conflict` with `RetryAfterSeconds`.
  - Maps `ACCOUNT_FROZEN` / `IP_NOT_ALLOWED` / `INVALID_API_KEY` (401/403) -> generic `ErrorType.ProviderUnavailable` whose message does NOT contain `"shop2topup"`, API keys, or raw provider error text (Review Focus #4).
- `BinanceExchangeRateProviderTests.cs`:
  - Calls `/api/v3/ticker/price?symbols=%5B%22BTCUSDT%22,%22ETHUSDT%22,%22USDCUSDT%22%5D`.
  - Under `es-VE` culture, parses decimal strings cleanly and computes `USD = 1m`, `USDT = 1m`, `USDC = 1m / usdcUsdt`, `BTC = 1m / btcUsdt`, `ETH = 1m / ethUsdt` (Review Focus #5).
  - Returns `ErrorType.ProviderUnavailable` on HTTP 500 or missing required symbols (never invents fake fallback rates).

- [ ] **Step 2: Run Infrastructure tests to verify they fail**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Infrastructure"`
Expected: FAIL.

- [ ] **Step 3: Implement Options, `UrlSanitizer`, `RedisHybridCacheService`, `Shop2TopUpCatalogProvider`, `BinanceExchangeRateProvider`, and `DependencyInjection`**

Note: Only cache **successful** upstream results in `ICacheService` (do not cache transient provider failures for 10–30 minutes; handle failure cleanly outside or bypass cache persistence on `Result.IsFailure`). Configure `HybridCache` + `StackExchangeRedisCache` conditionally so integration tests can override with in-memory distributed cache without needing a live Redis socket, while production requires `CacheOptions:RedisConnectionString`.

- [ ] **Step 4: Run Infrastructure tests to verify they pass**

Run: `dotnet test backend/tests/VerkkuGames.UnitTests --filter "FullyQualifiedName~Infrastructure"`
Expected: All Infrastructure tests PASS.

---

### Task 7: Frontend Server API Client, BFF Endpoints & SEO/Redirect Routes + Tests *(Wave 2 — Parallel)*

**Files:**
- Create: `frontend/src/lib/api-client.ts`
- Create: `frontend/src/pages/api/items.ts`
- Create: `frontend/src/pages/api/price.ts`
- Create: `frontend/src/pages/api/rates.ts`
- Create: `frontend/src/pages/robots.txt.ts`
- Create: `frontend/src/pages/sitemap.xml.ts`
- Create: `frontend/src/pages/index.astro`
- Create: `frontend/src/pages/404.astro`
- Test: `frontend/src/lib/__tests__/bff_and_seo_routes.test.ts`

**Interfaces:**
- Consumes: `src/lib/types.ts`, `src/lib/seo.ts`, `src/lib/regions.ts`, `src/i18n/*` (Task 4), and Backend API v1 contract (Spec Section 5.6).
- Produces:
  - `createServerApiClient(runtimeEnv, clientIp?)` in `src/lib/api-client.ts` reading `API_BASE_URL` and `API_CLIENT_KEY` exclusively from Cloudflare/Node server runtime env (`locals.runtime?.env` fallback `import.meta.env` / `process.env`), forwarding `X-Api-Client-Key` and `X-Forwarded-Client-Ip` (sourced from `CF-Connecting-IP`), with methods: `getBigCategories()`, `getCategories(params)`, `getItems(params)`, `getItemDetail(providerUid, itemId)`, `getItemPrice(providerUid, itemId)`, `getCategoryRequirements(providerUid, categoryId)`, `getRates()`.
  - Pure testable handler helpers for `/api/items`, `/api/price`, `/api/rates` (strict UUID validation for `providerUid`, alphanumeric validation for `itemId`/`categoryId`, numeric bounds for `page`/`pageSize`), `robots.txt.ts`, and `sitemap.xml.ts`.

- [ ] **Step 1: Write failing Bun tests for API client, BFF parameter validation, `robots.txt`, and `sitemap.xml`**

Pin:
- `api-client` attaches `X-Api-Client-Key` and `X-Forwarded-Client-Ip` headers and never exposes the key in returned errors.
- BFF `/api/price` rejects non-GUID `providerUid` (e.g., `"shop2topup"` or `"../admin"`) with `400` before calling upstream.
- BFF `/api/items` clamps/rejects invalid `pageSize > 100` or invalid `region`.
- `robots.txt` allows `/es/` & `/en/`, disallows `/api/`, and references `{PUBLIC_SITE_URL}/sitemap.xml`.
- `sitemap.xml` generates valid XML with `xhtml:link` `es`/`en`/`x-default` alternates using `providerUid`, falls back safely to static routes when backend is unreachable, and NEVER contains `"shop2topup"` (Review Focus #4).

- [ ] **Step 2: Run Bun tests to verify they fail**

Run: `bun test src/lib/__tests__/bff_and_seo_routes.test.ts` (in `frontend/`)
Expected: FAIL.

- [ ] **Step 3: Implement `api-client.ts`, `/api/{items,price,rates}.ts`, `robots.txt.ts`, `sitemap.xml.ts`, `index.astro`, and `404.astro`**

Ensure `index.astro` performs `302` redirect to `/es/` or `/en/` via `detectPreferredLocale`. Ensure `sitemap.xml.ts` sets `Cache-Control: public, max-age=900, stale-while-revalidate=300`.

- [ ] **Step 4: Run Bun tests to verify they pass**

Run: `bun test` (in `frontend/`)
Expected: All frontend tests PASS.

---

### Task 8: Backend API Layer (`VerkkuGames.Api`), Security, Dockerfile & Integration Tests *(Wave 3 — Parallel)*

**Files:**
- Create: `backend/src/VerkkuGames.Api/Options/ApiSecurityOptions.cs`
- Create: `backend/src/VerkkuGames.Api/Middleware/SecurityHeadersMiddleware.cs`
- Create: `backend/src/VerkkuGames.Api/Middleware/ApiClientKeyMiddleware.cs`
- Create: `backend/src/VerkkuGames.Api/Extensions/ResultExtensions.cs`
- Create: `backend/src/VerkkuGames.Api/Endpoints/CatalogEndpoints.cs`
- Create: `backend/src/VerkkuGames.Api/Endpoints/RatesEndpoints.cs`
- Create: `backend/src/VerkkuGames.Api/Endpoints/HealthEndpoints.cs`
- Create: `backend/src/VerkkuGames.Api/Program.cs`
- Create: `backend/src/VerkkuGames.Api/appsettings.json`
- Create: `backend/src/VerkkuGames.Api/appsettings.Development.json`
- Create: `backend/Dockerfile`
- Test: `backend/tests/VerkkuGames.IntegrationTests/VerkkuGamesApiFactory.cs`
- Test: `backend/tests/VerkkuGames.IntegrationTests/ApiSecurityAndRateLimitTests.cs`
- Test: `backend/tests/VerkkuGames.IntegrationTests/CatalogEndpointsTests.cs`

**Interfaces:**
- Consumes: `VerkkuGames.Domain` (Task 2), `VerkkuGames.Application` (Task 5), `VerkkuGames.Infrastructure` (Task 6).
- Produces:
  - REST v1 routes:
    - `GET /api/v1/catalog/big-categories`
    - `GET /api/v1/catalog/categories`
    - `GET /api/v1/catalog/items`
    - `GET /api/v1/catalog/items/{providerUid:guid}/{itemId}`
    - `GET /api/v1/catalog/items/{providerUid:guid}/{itemId}/price`
    - `GET /api/v1/catalog/categories/{providerUid:guid}/{categoryId}/requirements`
    - `GET /api/v1/rates`
    - `GET /health/live`
    - `GET /health/ready`
  - Public `partial class Program` for `WebApplicationFactory<Program>`.
  - ARM64/x64 multi-stage `backend/Dockerfile` running as non-root `app` user on port `8080`.

- [ ] **Step 1: Write failing Integration Tests covering API security, rate-limit partitioning, and all catalog/rates endpoints**

Pin:
- `ApiSecurityAndRateLimitTests.cs`:
  - Request to `/api/v1/rates` without `X-Api-Client-Key` (even with spoofed `X-Forwarded-Client-Ip: 1.2.3.4`) returns `401` ProblemDetails (Review Focus #2).
  - Request with wrong `X-Api-Client-Key` returns `401`.
  - `/health/live` succeeds (`200`) without `X-Api-Client-Key` and carries security headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`) with no `Server` header.
  - Exhausting per-visitor rate limit on `/price` with `X-Forwarded-Client-Ip: 203.0.113.10` returns `429` + `Retry-After` for `203.0.113.10`, while a concurrent request with `X-Forwarded-Client-Ip: 203.0.113.20` STILL returns `200` (Review Focus #2).
- `CatalogEndpointsTests.cs`:
  - Full happy path across `/big-categories`, `/categories`, `/items`, `/items/{providerUid}/{itemId}`, `/price`, `/requirements`, `/rates` using a fake `HttpMessageHandler` returning realistic SHOP2TOPUP & Binance JSON envelopes.
  - Response JSON contains `providerUid`, calculated retail USD price (NOT raw `unit_price`), and NEVER contains `"shop2topup"` (Review Focus #4).
  - Upstream `RELOAD_REQUIRED` (403) on `/price` returns clean ProblemDetails (`prices_locked`, no `"shop2topup"` leak) (Review Focus #1 & #4).
  - Upstream `ACCOUNT_FROZEN` / `IP_NOT_ALLOWED` returns sanitized `503` ProblemDetails with zero mention of `"shop2topup"`, keys, or upstream message (Review Focus #4).
  - Invalid query params (`page=0`, `pageSize=500`) return `400` Validation ProblemDetails.

- [ ] **Step 2: Run Integration Tests to verify they fail**

Run: `dotnet test backend/tests/VerkkuGames.IntegrationTests`
Expected: FAIL.

- [ ] **Step 3: Implement `VerkkuGames.Api` middleware, rate limiting, ProblemDetails mapping, Minimal API endpoints, `Program.cs`, appsettings, and `Dockerfile`**

Ensure middleware order is strictly:
1. `UseExceptionHandler` (RFC 9457 ProblemDetails, sanitized in non-Dev)
2. `SecurityHeadersMiddleware`
3. `UseCors` (strict origins from config, empty in prod)
4. `ApiClientKeyMiddleware` (guards `/api/v1/*` via `CryptographicOperations.FixedTimeEquals`; stores verified flag on `HttpContext.Items`)
5. `UseRateLimiter` (partitions by `X-Forwarded-Client-Ip` ONLY when `HttpContext.Items` confirms authenticated BFF key; fallback to `Connection.RemoteIpAddress`; plus global safety limiter)
6. Endpoints + OpenAPI only when `app.Environment.IsDevelopment()`.
Disable Kestrel `Server` header (`options.AddServerHeader = false`).

- [ ] **Step 4: Run all Backend tests (Unit + Integration)**

Run: `dotnet test backend/VerkkuGames.slnx`
Expected: 100% PASS, zero warnings.

---

### Task 9: Frontend Astro Layouts, React Islands & Bilingual Pages *(Wave 3 — Parallel)*

**Files:**
- Create: `frontend/src/layouts/BaseLayout.astro`
- Create: `frontend/src/components/astro/Navbar.astro`
- Create: `frontend/src/components/astro/Footer.astro`
- Create: `frontend/src/components/astro/Breadcrumbs.astro`
- Create: `frontend/src/components/astro/HeroBanner.astro`
- Create: `frontend/src/components/astro/CategoryCard.astro`
- Create: `frontend/src/components/react/CurrencyStore.ts`
- Create: `frontend/src/components/react/CurrencySwitcher.tsx`
- Create: `frontend/src/components/react/RegionSelector.tsx`
- Create: `frontend/src/components/react/PriceDisplay.tsx`
- Create: `frontend/src/components/react/CatalogExplorer.tsx`
- Create: `frontend/src/components/react/ProductDetailPanel.tsx`
- Create: `frontend/src/pages/[lang]/index.astro`
- Create: `frontend/src/pages/[lang]/catalog/index.astro`
- Create: `frontend/src/pages/[lang]/category/[providerUid]/[id].astro`
- Create: `frontend/src/pages/[lang]/product/[providerUid]/[id].astro`
- Create: `frontend/src/pages/[lang]/studio/index.astro`

**Interfaces:**
- Consumes: `src/lib/{types,currency,regions,seo,api-client}.ts`, `src/i18n/*` (Tasks 4 & 7), `/assets/logo.svg`, `/assets/placeholder-product.svg`, `/api/{items,price,rates}`.
- Produces: Complete SSR bilingual storefront for `/es/...` and `/en/...` with Verkku brand styling, live crypto currency switching (`USD`, `USDT`, `USDC`, `BTC`, `ETH`), prominent region selector (`USA`, `GLOBAL`, `MX`, `PA`, `CO`), clean error states when API is unreachable/unconfigured (no fake products), and read-only *"Próximamente / Coming soon"* CTA on product pages.

- [ ] **Step 1: Implement `BaseLayout.astro` and Astro UI components (`Navbar`, `Footer`, `Breadcrumbs`, `HeroBanner`, `CategoryCard`)**

- `BaseLayout.astro`: `<html lang={locale}>`, canonical, `hreflang` (`es`, `en`, `x-default`), OpenGraph, Twitter Cards, JSON-LD injection slot, skip-to-content link, dark forest `#05342E` theme.
- `Navbar.astro`: Renders `/assets/logo.svg` + `GAMES` badge, links to Catalog & Studio, language toggle (`ES | EN`), and mounts `CurrencySwitcher` island (`client:load`).
- `HeroBanner.astro`: Gamer-natural copy with Verkku's authentic voice + quick region entry pills (`USA`, `Global`, `México`, `Panamá`, `Colombia`).
- `CategoryCard.astro`: Uses `sanitizeExternalImageUrl`, `loading="lazy"`, `referrerpolicy="no-referrer"`, region badge, links to `/{lang}/category/{providerUid}/{externalId}`.

- [ ] **Step 2: Implement React interactive islands (`CurrencyStore`, `CurrencySwitcher`, `RegionSelector`, `PriceDisplay`, `CatalogExplorer`, `ProductDetailPanel`)**

- `CurrencyStore.ts`: Lightweight reactive store (custom event + `localStorage` key `verkku_currency`) syncing selected currency across islands on the page, fetching `/api/rates` lazily when needed (or hydrating from SSR initial rates).
- `RegionSelector.tsx`: Prominent selector for `USA`, `GLOBAL`, `MX`, `PA`, `CO` (+ `ALL`/`OTHER`) with an *"Incluir Global / Include Global"* toggle when a country is chosen.
- `CatalogExplorer.tsx`: Hydrates from SSR initial page of items, fetches `/api/items` on filter/search/region/type/stock/pagination change, renders honest empty/error/prices-locked states (never fake items).
- `ProductDetailPanel.tsx`: Shows product image (sanitized), region badge, cross-region sibling category switcher links, sibling denomination selector within the category, live price refresh button calling `/api/price` with USD + selected crypto currency display, dynamic top-up requirements preview (when `requirements.length > 0`), stock badge, and read-only **"Próximamente / Coming soon"** CTA banner.

- [ ] **Step 3: Implement all bilingual pages under `src/pages/[lang]/`**

Validate `Astro.params.lang` (return 404 if not `es|en`), validate `providerUid` as UUID on category/product routes (return 404 on non-UUID), render full SSR data + JSON-LD (`WebSite`, `Organization`, `BreadcrumbList`, `Product`), and handle upstream API failures gracefully with clear localized notices (no crashes, no fake products).

- [ ] **Step 4: Run TypeScript/Astro checks, unit tests, and Cloudflare Workers production build**

Run (in `frontend/`):
- `bun test`
- `bun run check` (`astro check`)
- `bun run build`
Expected: Zero TypeScript/Astro errors, all tests PASS, clean `dist/_worker.js` build for Cloudflare Workers.

---

### Task 10: Final End-to-End Verification, Security Audits & Clean Git Commit *(Wave 4 — Orchestrator)*

**Files:**
- Verify: entire `F:\Verkku\local\verkku-games\` workspace

- [ ] **Step 1: Run full Backend verification & NuGet vulnerability audit**

Run:
- `dotnet build backend/VerkkuGames.slnx --configuration Release`
- `dotnet test backend/VerkkuGames.slnx --configuration Release`
- `dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive`
Expected: Zero warnings/errors, all Unit & Integration tests PASS, zero vulnerable packages.

- [ ] **Step 2: Run full Frontend verification & Bun security audit**

Run (in `frontend/`):
- `bun test`
- `bun run check`
- `bun run build`
- `bun audit`
Expected: All tests PASS, zero type/Astro errors, clean build, zero vulnerabilities.

- [ ] **Step 3: Run privacy/leakage & asset compliance checks**

Verify:
- `frontend/public/assets/logo.svg` exists and matches the provided Verkku SVG.
- Grep across `frontend/src/` and `backend/src/VerkkuGames.Api/` confirms zero occurrences of `"shop2topup"` (case-insensitive) outside internal Infrastructure/config/docs.
- Zero `.env` or secret values tracked in `git status`.

- [ ] **Step 4: Final clean commits & handoff summary**

Commit clean logical commits (or verify each wave's commits), summarize architecture, test counts, security audit outputs, and exact commands to run locally with Redis + SHOP2TOPUP credentials.

---

## Self-Review Notes

- **Spec coverage:** Every requirement in `docs/superpowers/specs/2026-11-25-verkku-games-v1-design.md` maps to a concrete task: Clean Architecture 4 layers + Unit/Integration tests (Tasks 1, 2, 5, 6, 8), opaque `ProviderUid` (Tasks 2, 5, 6, 7, 8, 9), SHOP2TOPUP real integration & error codes (Task 6), Binance `data-api.binance.vision` crypto rates & USD base without Bs (Tasks 2, 4, 6), Redis/Valkey `HybridCache` (Tasks 1, 6), pricing floor math (Task 5), region resolution & `includeGlobal` toggle (Tasks 5, 9), BFF + post-auth per-visitor rate limiting (Tasks 7, 8), Astro 5 Cloudflare Workers SSR + React islands + ES/EN i18n + SEO + `logo.svg` + "Próximamente" CTA (Tasks 1, 4, 7, 9), Bun `1.4.5` hardening & security audits (Tasks 1, 10), READMEs/rules/agents (Task 3).
- **Type consistency:** `ProviderUid` (`Guid`), `RegionCode`, `CurrencyCode`, `ProductType`, `CatalogItemDetail`, `ItemPriceQuote`, `ExchangeRatesSnapshot`, and all REST `/api/v1/*` paths match identically across Tasks 2, 4, 5, 6, 7, 8, and 9.
- **Review Focus coverage:** All 5 high-risk failure modes are explicitly pinned to unit/integration tests in Tasks 2, 4, 6, 7, and 8.
