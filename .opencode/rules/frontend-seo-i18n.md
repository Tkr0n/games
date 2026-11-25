# Rule: Frontend, SEO & i18n

Applies to `frontend/**`. Astro 7 SSR on Cloudflare Workers, React 19 islands, Tailwind v4, Bun 1.4.5, native ES/EN.

## 1. Rendering model

**DO**

- Render content, links, headings, breadcrumbs and metadata in `.astro` (server HTML).
- Use React islands only for interactive controls: `CurrencySwitcher`, `RegionSelector`, `CatalogExplorer`, `ProductDetailPanel`.
- Choose the weakest hydration directive that works: `client:visible` > `client:idle` > `client:load`.
- Fetch catalogue data server-side via `src/lib/api-client.ts` (BFF/SSR).
- Keep islands small, typed and independent.
- Read `localStorage` inside `useEffect`, never during initial render.

**DO NOT**

- Put indexable content inside React components or `client:only`.
- Fetch the Raspberry Pi from the browser; call `/api/*` (BFF) only.
- Add a client-side router, global state library, or SPA shell.
- Hydrate the whole page or nest island roots.

## 2. Astro + Cloudflare

**DO**

- Keep `output: 'server'` and the `@astrojs/cloudflare` adapter (`imageService: 'passthrough'`).
- Keep `wrangler.jsonc` as Workers with Static Assets (`main`, `assets.binding: ASSETS`), `nodejs_compat`, `compatibility_date` current.
- Declare only non-secret values in `vars` (`PUBLIC_SITE_URL`, `API_BASE_URL`).
- Store `API_CLIENT_KEY` with `wrangler secret put`.
- Keep `security.checkOrigin: true`.
- Validate BFF inputs: `providerUid` is a `Guid`, `pageSize` capped, no passthrough of arbitrary query params.

**DO NOT**

- Switch to `output: 'static'` for catalogue pages.
- Add `PUBLIC_` to secrets.
- Change `main` to a local file or add a second adapter.
- Hardcode `https://games.verkku.com` outside configuration fallbacks.

## 3. i18n (ES/EN)

**DO**

- Keep `es` as default with `prefixDefaultLocale: true`; all routes live under `/es/` and `/en/`.
- Keep `src/i18n/es.ts` as the typed source of truth; `en.ts` must satisfy the same type (key parity).
- Add every new string to **both** dictionaries in the same change.
- Use the typed translator + helpers from `src/i18n/utils.ts`.
- Localize dates/numbers/currency with `Intl` and the active locale.
- Redirect `/` with `302` based on cookie preference or `Accept-Language`, fallback `/es/`.
- Keep the voice natural, close and gamer, while technically honest.

**DO NOT**

- Hardcode user-visible copy in `.astro` or `.tsx` files.
- Concatenate translated fragments into sentences; use placeholders.
- Translate brand names, `Gift Card`, region codes, or ticker symbols (`BTC`, `USDT`).
- Ship a locale with missing keys, `TODO`, or English copy in the ES bundle.

## 4. SEO

**DO**

- Emit absolute `canonical` from `PUBLIC_SITE_URL` on every page.
- Emit reciprocal `hreflang` for `es`, `en`, and `x-default`.
- Set `<html lang>` to the active locale.
- Generate `robots.txt` dynamically and disallow `/api/`.
- Generate `sitemap.xml` with bilingual `xhtml:link` alternates and canonical routes only.
- Add OpenGraph + Twitter Cards (`og:title`, `og:description`, `og:image`, `og:url`, `twitter:card`).
- Add JSON-LD `Organization`, `WebSite`, `BreadcrumbList`, and `Product`/`Offer` only when a real retail price exists.
- Use unique titles (≤ 60 chars) and descriptions (≤ 160 chars) per locale.
- Use `ProviderUid` in all catalogue URLs: `/[lang]/category/{providerUid}/{id}`, `/[lang]/product/{providerUid}/{id}`.

**DO NOT**

- Mention the wholesale provider (`"shop2topup"`) in URLs, meta, JSON-LD, HTML or `sitemap.xml`.
- Emit an `Offer` with a null/locked price or a reseller cost.
- Index `/api/*`, error pages, or duplicate filter permutations.
- Use client-side-only meta tags or relative canonicals.
- Add `noindex` to canonical catalogue pages.

## 5. Brand, Tailwind v4 and accessibility

**DO**

- Use the official palette: base `#05342E`/`#06352F`, surface `#0A453D`/`#0E4F46`, brand `#1A8154`/`#209959`, accents `#25C877`/`#81CB79`/`#8BCCAB`, teal `#2E6D6D`, light `#FBF7F5`/`#EFF0E9`.
- Use `Bricolage Grotesque` for headings and `Poppins` for body with `display=swap`.
- Use `/assets/logo.svg` (exact filename) plus the `GAMES` badge in the navbar.
- Fall back to `/assets/placeholder-product.svg` for missing/invalid images.
- Meet WCAG AA: 4.5:1 for body text, 3:1 for large text and UI boundaries.
- Provide visible focus states, semantic landmarks, one `h1` per page, and `alt` text.
- Respect `prefers-reduced-motion`.
- Keep the v1 CTA as a read-only "Próximamente" / "Coming soon" state (not a disabled fake button that implies checkout).

**DO NOT**

- Invent colors, gradients or fonts outside the brand palette.
- Use accent green as small text on the dark base without checking contrast.
- Use color alone to convey stock/region/price state.
- Add cart, checkout, "Comprar", order creation, or WhatsApp/Telegram sales links.
- Show Bs/VES anywhere.

## 6. Currency and numbers

**DO**

- Always show USD as the reference (`$`, 2 decimals).
- Show one crypto equivalent at a time, persisted in `localStorage`: `USDT` 2, `USDC` 2, `BTC` 8, `ETH` 6 decimals, fixed-point (never scientific notation).
- Format with `Intl.NumberFormat` and explicit fraction digits.
- Treat `null` price (locked) as an explicit "precio no disponible" state.

**DO NOT**

- Use `toFixed` alone for tiny BTC amounts without verifying the output (`0.00000519`, not `5.19e-6`).
- Sum or convert with JS floating-point for display of authoritative totals; use integer minor units or decimal strings.
- Round crypto to 2 decimals or USD to 0.

## 7. Bun and dependencies

**DO**

- `bun install --frozen-lockfile` in CI/deploy; commit `bun.lock`.
- Keep `bunfig.toml` `minimumReleaseAge = 604800` and `exact = true`.
- Run `bun audit` (0 vulnerabilities), `bun test`, `bun run check`, `bun run build`.
- Justify any new dependency (license, maintenance, bundle cost).

**DO NOT**

- Bypass the 7-day cooldown or use floating versions.
- Add a UI kit, icon pack or state library that duplicates Astro/React/Tailwind.
- Commit `.astro/`, `dist/`, `.wrangler/`, `node_modules/`, `.dev.vars`.

## Pre-completion checklist

- [ ] `bun test` green.
- [ ] `bun run check` clean (types + `.astro`).
- [ ] `bun run build` succeeds.
- [ ] `bun audit` → 0.
- [ ] ES/EN key parity verified.
- [ ] `canonical`, `hreflang`, `<html lang>`, OG and JSON-LD present and correct.
- [ ] `/robots.txt` blocks `/api/`; `/sitemap.xml` has no provider name.
- [ ] No hardcoded copy, no `PUBLIC_` secrets, no Bs/VES, no checkout CTA.
- [ ] Contrast and keyboard navigation checked on new UI.
