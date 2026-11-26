# FazerCards Game Keys Provider — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second catalog provider (`FazerCards`) that sells **full game keys** — a new `GameKey` product type with base/DLC/season-pass variants and real region-lock metadata — using the existing `ICatalogProvider` + opaque `ProviderUid` architecture, and surface a "Juegos completos" entry point in the storefront.

**Architecture:** .NET 10 Clean Architecture (unchanged). Add `GameKey` to the `ProductType` enum + a `GameKeyVariant` value object + region-lock metadata on `CatalogItem`. Implement `FazerCardsCatalogProvider` (REST v2, `X-API-Key`, snake_case `data` envelopes, cursor pagination) behind a distinct opaque `ProviderUid`. Application queries already accept `productType` and `region`, so filtering works with no API changes. Frontend adds a third quick-filter card and a `GameKey` badge; regions already come from backend metadata.

**Tech Stack:** .NET 10 (`10.0.402` pinned), `Microsoft.Extensions.Http.Resilience`, `HybridCache`/Redis, FluentValidation, xUnit + NSubstitute; Astro 7 + React 19 + Tailwind v4 + Bun 1.4.5.

**Spec:** `docs/design/2026-11-25-verkku-games-v1-design.md` (original) + this plan's "Contract Facts" section (live-verified FazerCards API v2).

---

## Credenciales necesarias (usuario)

- `FAZERCARDS_API_KEY` — API key del hub de FazerCards (Profile → API keys). Se pone en `.env` (raíz y `backend/.env.example`) y en variables del compose `FAZERCARDS_API_KEY`.
- `FAZERCARDS_PROVIDER_UID` — GUID opaco nuevo (distinto al de SHOP2TOPUP). Lo genero yo y lo documento; el usuario solo lo confirma.
- Opcional para prueba real: saldo en la wallet de FazerCards (para `POST /gamekeys/order`). **No requerido en v1** (solo lectura de catálogo), igual que con SHOP2TOPUP.

## Contract Facts (live-verified from FazerCards docs)

Base URL: `https://api.fzr.cards/api/v2`. Auth: `X-API-Key: YOUR_API_KEY` (o `Authorization: Bearer`). Todas las respuestas llevan `"ok": true` / `"ok": false` + `"error"`/`"code"`.

- `GET /gamekeys?limit=50&cursor=…` → `{ ok, kind:"game_key", items:[{ name, game_id, region, platform, region_restriction }], meta:{ total, limit, next_cursor, has_more } }`
- `GET /gamekeys/keys?game_id={id}` → `{ ok, kind:"game_key", game_id, GameName (capitalizado históricamente), region, platform, region_restriction, keys:[{ key_id, name, price_usd, stock, min_order_quantity, max_order_quantity }] }`
- `GET /gamekeys/region-restriction?game_id={id}` → `{ ok, kind:"game_key", game_id, region_type, has_availability, available:[{code,name}], unavailable:[{code,name}] }` (cuando `region_restriction=true`)
- `GET /me` → `{ ok, login, email, plan, planExpiresAt, subscriptionActive, summary:{...} }` (health check; **nunca exponer email/wallet**)
- Catálogo mixto snake_case (`game_id`, `region_restriction`, `price_usd`) + camelCase en cuenta (`planExpiresAt`).
- Errores HTTP estándar: 400 validación, 401 key inválida, 403 producto/plan no habilitado, 404, 429 (`Retry-After`), 5xx.
- Rate limits: catálogo 120/min; órdenes 60/min; cuenta 30/min. Cachear catálogo 5–15 min.

---

## Global Constraints

- Opaque `ProviderUid`: `"fazercards"` / `X-API-Key` **nunca** en URLs públicas, JSON público, HTML, JSON-LD ni sitemap. Solo en configuración/DI/logs internos.
- Dinero = `decimal`, `CultureInfo.InvariantCulture`. Monedas: USD/USDT/USDC (2 dec), ETH (6 dec), BTC (8 dec). Sin Bs/VES. `price_usd` es string decimal en los SKUs.
- Cero mocks/fallbacks en runtime: si FazerCards falla o no hay key, error controlado (`ProblemDetails`) — nunca juegos falsos.
- Sin MediatR/AutoMapper. Sin `float`/`double` para dinero.
- CTA v1 sigue siendo solo lectura ("Próximamente"). Sin órdenes/checkout aún.
- El `ProviderUid` de FazerCards debe ser distinto al de SHOP2TOPUP (`d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42`). Lo genero yo (nuevo Guid) y se documenta.
- Los literales `"fazercards"` y `"X-API-Key"` solo en Infrastructure/config/docs (grep de fuga limpio en superficie pública).

## Review Focus

1. **Region-lock death trap:** una key `region_restriction=true` con `region_type="CIS"` debe mostrar solo países disponibles; el comprador debe ver el badge de región antes de comprar. Test: `GetGameKeys_WithRegionRestriction_MapsAvailableCountries`.
2. **`GameName` capitalizado + IDs snake_case:** el parser debe leer `GameName`, `game_id`, `key_id`, `price_usd` sin caer en `null`. Test: `GetItems_ParsesHistoricalGameNameAndSnakeCaseIds`.
3. **Fuga del proveedor en errores:** 401/403/5xx de FazerCards → `Error.ProviderUnavailable` genérico sin `X-API-Key`, email del reseller ni `"fazercards"`. Test: `SensitiveErrors_ReturnSanitizedProviderUnavailable`.
4. **Cursor pagination:** `has_more=true` con `next_cursor` debe dar page+1 y no perder items; `null` next_cursor con `has_more=true` no debe loop infinito. Test: `GetItems_FollowsCursorAndStops`.
5. **Precio retail vs reseller:** `price_usd="19.9900"` debe pasar por `IPricingService` (retail 21.99 con margen 10% + piso 0.25), nunca exponer 19.99. Test: `GetItems_AppliesMarkupNeverLeaksWholesalePrice`.

---

## File Structure

```
backend/src/VerkkuGames.Domain/ValueObjects/GameKeyVariant.cs        (new)
backend/src/VerkkuGames.Domain/Catalog/CatalogItem.cs                (modify: + variant/region-lock fields)
backend/src/VerkkuGames.Domain/Catalog/CatalogItemDetail.cs          (modify if needed)
backend/src/VerkkuGames.Application/Abstractions/ICatalogProvider.cs (no change — same interface)
backend/src/VerkkuGames.Infrastructure/Options/FazerCardsOptions.cs  (new)
backend/src/VerkkuGames.Infrastructure/Providers/FazerCards/FazerCardsDtos.cs       (new)
backend/src/VerkkuGames.Infrastructure/Providers/FazerCards/FazerCardsCatalogProvider.cs (new)
backend/src/VerkkuGames.Infrastructure/DependencyInjection.cs        (modify: register 2nd provider)
backend/tests/VerkkuGames.UnitTests/Infrastructure/FazerCardsCatalogProviderTests.cs (new)
backend/src/VerkkuGames.Api/appsettings.json / .env.example / docker-compose.yml    (modify: config)
frontend/src/lib/types.ts                       (modify: GameKey variant fields)
frontend/src/i18n/{config,es,en}.ts             (modify: "Juegos completos" labels)
frontend/src/components/react/CatalogExplorer.tsx (modify: GameKey badge)
frontend/src/pages/[lang]/index.astro           (modify: 3rd quick-filter card)
docs/design/2026-11-25-verkku-games-v1-design.md (modify: appendix — 2nd provider)
```

---

## Tasks

### Task 1: Domain — `GameKeyVariant` + extend `CatalogItem`

**Files:**
- Create: `backend/src/VerkkuGames.Domain/ValueObjects/GameKeyVariant.cs`
- Modify: `backend/src/VerkkuGames.Domain/Catalog/CatalogItem.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Domain/GameKeyVariantTests.cs`

**Interfaces:**
- Consumes: nothing (Domain is dependency-free).
- Produces:
  - `public enum GameKeyVariant { BaseGame, Dlc, SeasonPass, Unknown }`
  - `public sealed record RegionLockInfo(string RegionType, IReadOnlyList<string> AvailableCountryCodes, IReadOnlyList<string> UnavailableCountryCodes)` in `VerkkuGames.Domain.Catalog`.
  - `CatalogItem` gains: `GameKeyVariant? KeyVariant`, `RegionLockInfo? RegionLock`, `string? Platform` (e.g. `"steam"`, `"xbox"`), with defaults `null` so existing SHOP2TOPUP records keep compiling.

- [ ] **Step 1: Write failing tests** for `GameKeyVariant` enum completeness and `RegionLockInfo` immutability (empty lists when no restriction).
- [ ] **Step 2:** `dotnet test ... --filter "FullyQualifiedName~GameKeyVariant"` → FAIL (types absent).
- [ ] **Step 3:** Implement the enum + record; extend `CatalogItem` with optional trailing params.
- [ ] **Step 4:** `dotnet test` → PASS; `dotnet build` → 0 warnings.
- [ ] **Step 5:** Commit.

### Task 2: Infrastructure — `FazerCardsOptions` + DTOs + provider

**Files:**
- Create: `backend/src/VerkkuGames.Infrastructure/Options/FazerCardsOptions.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/FazerCards/FazerCardsDtos.cs`
- Create: `backend/src/VerkkuGames.Infrastructure/Providers/FazerCards/FazerCardsCatalogProvider.cs`
- Test: `backend/tests/VerkkuGames.UnitTests/Infrastructure/FazerCardsCatalogProviderTests.cs`

**Interfaces:**
- Consumes: `ICatalogProvider`, `IPricingService`, `IRegionResolver`, `ICacheService`, `Money`, `CatalogItem`, `Result<T>`, `Error`.
- Produces: `FazerCardsCatalogProvider : ICatalogProvider` with `InternalKey => "fazercards"` (internal only) and `ProviderUid` from options.

- [ ] **Step 1: Write failing tests** (fake `HttpMessageHandler`):
  - `GetBigCategories_MapsGameCatalog` — `GET gamekeys?limit=50` with `X-API-Key` header; maps items to `CatalogBigCategory` (use `game_id` as ExternalId, `name`, `region` description).
  - `GetCategories_ForGameId_MapsSkus` — `GET gamekeys/keys?game_id=gk_x`; maps `keys[]` to `CatalogItem` with `ProductType=TopUp` (v1 reuse since no GameKey in public API enum yet — see note below), `KeyVariant` from `name` (`Standard edition`→BaseGame, `DLC`→Dlc, `Season pass`/`Deluxe`→SeasonPass), `Platform`, `RetailPriceUsd` after markup.
  - `GetItems_AppliesMarkupNeverLeaksWholesalePrice` — `price_usd="19.9900"` → retail `21.99m`, never `19.99m`.
  - `GetCategoryRequirements_FromRegionRestriction` — `region_restriction=true` → `GET gamekeys/region-restriction?game_id=…`, map `available[].code` to region metadata in `Description` (no new domain type needed).
  - `SensitiveErrors_ReturnSanitizedProviderUnavailable` — 401/403/5xx with body containing email/key → generic error, no `"fazercards"`, no `"X-API-Key"`, no secret.
  - `GetItems_FollowsCursorAndStops` — first page `has_more=true, next_cursor="opaque"`, second `has_more=false` → 2 HTTP calls, all items, no third call.
  - `CheckHealth_UsesMeWithoutLeakingEmail` — `GET /me` → `Success(true)`, never exposes email/login.
- [ ] **Step 2:** `dotnet test ... --filter "FullyQualifiedName~FazerCards"` → FAIL (types absent).
- [ ] **Step 3:** Implement options (`SectionName="FazerCards"`, `BaseUrl="https://api.fzr.cards/api/v2"`, `ApiKey`, `ProviderUid` = new Guid, `TimeoutSeconds=8`, `[Required]`, `ValidateOnStart`) and DTOs (`snake_case` `[JsonPropertyName]`, `decimal?` for `price_usd`, `List<…>` for keys/available/unavailable, `bool HasMore`, `string? NextCursor`) and provider (same cache/error patterns as `Shop2TopUpCatalogProvider`, `Authorization: Bearer` or `X-API-Key`, cursor loop with max 5 pages guard, `GameName` read from `JsonPropertyName("GameName")`).
- [ ] **Step 4:** `dotnet test` → PASS; build 0 warnings.
- [ ] **Step 5:** Commit.

> **Public enum note:** `ProductType` stays `TopUp | Voucher` in v1 to avoid a breaking API change. Game keys are surfaced as `topup` with the `KeyVariant` badge in JSON (`keyVariant`, `platform`, `regionLock`) so the frontend can show "Juego completo · Steam · Base/DLC". If the user later wants `GameKey` as a third public filter value, that is a separate additive change.

### Task 3: DI + config — register second provider

**Files:**
- Modify: `backend/src/VerkkuGames.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/VerkkuGames.Api/appsettings.json`
- Modify: `F:\Verkku\local\verkku-games\.env.example`, `backend/.env.example`, `docker-compose.yml`
- Test: extend `backend/tests/VerkkuGames.UnitTests/Infrastructure/FazerCardsCatalogProviderTests.cs` (DI smoke test).

**Interfaces:**
- Consumes: options/provider from Task 2.
- Produces: `IEnumerable<ICatalogProvider>` contains **two** providers with distinct `ProviderUid`s; `ICatalogProviderRegistry.GetAll()` returns both.

- [ ] **Step 1: Write failing DI test** — build ServiceCollection with both keys, resolve `GetServices<ICatalogProvider>()`, assert 2 providers and distinct UIDs.
- [ ] **Step 2:** Test → FAIL.
- [ ] **Step 3:** In `AddInfrastructure`, bind/validate `FazerCardsOptions`, `AddHttpClient<FazerCardsCatalogProvider>` with resilience (same transient-only policy), register as `ICatalogProvider`. Add safe defaults to `appsettings.json` (empty `ApiKey` → fail-fast via `ValidateOnStart` in prod) and env templates.
- [ ] **Step 4:** Full `dotnet test VerkkuGames.slnx` → PASS (all existing SHOP2TOPUP tests still pass — 2 providers now registered).
- [ ] **Step 5:** Commit.

### Task 4: Frontend — "Juegos completos" quick filter + GameKey badges

**Files:**
- Modify: `frontend/src/lib/types.ts`
- Modify: `frontend/src/i18n/config.ts`, `es.ts`, `en.ts`
- Modify: `frontend/src/components/react/CatalogExplorer.tsx`
- Modify: `frontend/src/pages/[lang]/index.astro`

**Interfaces:**
- Consumes: `CatalogItemDto` from API with new optional `keyVariant`, `platform`, `regionLock`.
- Produces: third quick-filter card linking to `/catalog?providerUid={fazerUid}&productType=topup` (filtered by provider), and a badge showing variant/platform on game-key items.

- [ ] **Step 1: Write failing Bun test** — i18n parity with new `home.typeFullGames*` keys; `CatalogExplorer` renders variant badge when `keyVariant` present (or a pure helper `formatKeyVariant` unit test).
- [ ] **Step 2:** `bun test` → FAIL (missing keys/types).
- [ ] **Step 3:** Add optional fields to `CatalogItemDto`, add i18n keys in both locales, add 3rd card on home (`🎲 Juegos completos`), add `KeyVariant` badge in CatalogExplorer cards. The `providerUid` for the card comes from a public config constant documented in `.env.example` as `NEXT_PUBLIC_GAMEKEYS_PROVIDER_UID` (the opaque GUID, not a secret).
- [ ] **Step 4:** `bun test`, `bun run check`, `bun run build` → PASS.
- [ ] **Step 5:** Commit.

### Task 5: Docs + verification

**Files:**
- Modify: `docs/design/2026-11-25-verkku-games-v1-design.md` (append "Appendix B — FazerCards second provider")
- Modify: `README.md`, `backend/README.md`, `frontend/README.md` (brief: multi-provider, game keys)
- Report: `.superpowers/sdd/verkku-games-v1/task-fazercards-report.md`

- [ ] **Step 1:** Document the second provider, credentials, contract facts, region-lock policy, and refund warning.
- [ ] **Step 2:** Full verification: `dotnet build`/`dotnet test`/NuGet audit; `bun test`/`check`/`build`/`audit`; leak grep (`fazercards`, `X-API-Key`, `shop2topup` in public surface).
- [ ] **Step 3:** Commit + push.

---

## Execution Handoff

El usuario ya autorizó "adelante ve integrando". Ejecuto **Native** (yo mismo en esta sesión), verificando cada task con tests reales. Si aparece una ambigüedad estructural, la resuelvo con un ruling documentado y sigo (no me detengo a preguntar).
