# Appendix B — Second catalog provider: FazerCards (full game keys)

Decided 2026-11-26. Implemented per `docs/design/2026-11-26-fazercards-gamekeys-plan.md`.

## Why
SHOP2TOPUP delivers top-ups and gift cards; full-game keys are a small exception
(Minecraft Java & Bedrock). FazerCards REST API v2 exposes 357+ full games with
region-lock metadata, crypto payments (USDT / Binance Pay), webhooks and a free
trial — a good fit for the existing `ICatalogProvider` abstraction.

## Identity & isolation
- `ProviderUid` (opaque, public): `9075f324-4177-45e9-a79c-f23722258b8f`.
- `InternalKey` (private): `fazercards` — only in config/DI/internal logs.
- NEVER leak `fazercards` / `X-API-Key` / `FazerCards` into public URLs, JSON, HTML,
  OpenGraph, JSON-LD or sitemap. Public references use the opaque GUID only.
- Credentials: `FAZERCARDS_API_KEY` + `FAZERCARDS_PROVIDER_UID` in `.env` (gitignored)
  and `FazerCards__*` env vars in the compose `api` service.

## Live-verified contract (base `https://api.fzr.cards/api/v2`)
- Auth: `X-API-Key: {key}`. Success: `{"ok":true,...}`. Errors: `{"ok":false,"error":...,"code":...}`.
- `GET /gamekeys?limit=50&cursor=…` → `{ items:[{name, game_id, region, platform, region_restriction}], meta:{total,limit,next_cursor,has_more} }`
- `GET /gamekeys/keys?game_id=…` → `{ GameName (capitalized), region, platform, region_restriction, keys:[{key_id,name,price_usd,stock}] }`
- `GET /gamekeys/region-restriction?game_id=…` → `{ region_type, has_availability, available:[{code,name}], unavailable:[...] }`
- `GET /me` → health only (email/login NEVER exposed publicly).

## Design decisions
- Item ExternalId is the composite `{game_id}--{key_id}` (matches `^[A-Za-z0-9_-]+$`),
  enabling direct lookup via the new `ICatalogProvider.GetItemByIdAsync` (default
  interface implementation scans the listing for legacy providers).
- `ProductType` stays `TopUp | Voucher` in v1 to avoid a breaking API change. Game
  items carry `keyVariant` (0=BaseGame, 1=Dlc, 2=SeasonPass), `platform` and
  `regionLock` so the frontend can badge them as full games.
- Unfiltered `GetItemsAsync(null)` returns the first page of games (50) to avoid
  357+ HTTP calls; direct product URLs resolve their game via `GetItemByIdAsync`.
  Redis caches results 10 min; first cold browse can take ~30–60 s.
- `price_usd` is parsed as invariant-culture decimal and passes through
  `IPricingService` — the wholesale value is never returned.
- FazerCards is registered only when `ApiKey` is configured; the API boots with
  just SHOP2TOPUP if the key is absent.
- v1 is read-only: no orders, no checkout, no webhook processing (v2).

## Business warning
Game keys are single-use and often region-locked (`region_restriction=true`).
Show the region badge before checkout; define a clear refund policy in v2.
