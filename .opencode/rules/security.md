# Rule: Security

Applies to the whole repo. Non-negotiable. When in doubt, choose the more restrictive option.

## 1. Trust boundaries

```text
Browser ──HTTPS──► Cloudflare Worker (Astro SSR + BFF /api/*) ──Tunnel+key──► Raspberry Pi API
```

**DO**

- Keep `API_BASE_URL` and `API_CLIENT_KEY` server-only. In Astro they must **not** be prefixed `PUBLIC_`.
- Send `X-Api-Client-Key` from the Worker only; validate it in .NET with `CryptographicOperations.FixedTimeEquals`.
- Forward the visitor IP as `X-Forwarded-Client-Ip`, derived from `CF-Connecting-IP`.
- Authenticate **before** trusting `X-Forwarded-Client-Ip`; partition rate limits by the final client IP after auth.
- Bind the API to `127.0.0.1` (or the Docker internal network) and expose it only through Cloudflare Tunnel.
- Set `API_CLIENT_KEY` with `wrangler secret put API_CLIENT_KEY`; `VALKEY_PASSWORD` and `Shop2TopUp__*` only in env/User Secrets.

**DO NOT**

- Call the Raspberry Pi from the browser, `client:*` islands, or any `PUBLIC_*` variable.
- Expose port 6379/8080 to LAN/WAN in production.
- Accept `X-Forwarded-Client-Ip` from unauthenticated requests (must be `401` first).
- Put secrets in `wrangler.jsonc` `vars`, `appsettings.json`, READMEs, tests snapshots, or logs.
- Log `Authorization`, `X-Api-Client-Key`, `KeySecret`, `unit_price`, or full provider envelopes.

## 2. Supply chain

**DO**

- Frontend: `bunfig.toml` with `minimumReleaseAge = 604800`; install with `bun install --frozen-lockfile` in CI/deploy; commit `bun.lock`.
- Backend: Central Package Management in `Directory.Packages.props` (`ManagePackageVersionsCentrally`, `CentralPackageTransitivePinningEnabled`); pin exact versions; audit transitives.
- Keep `trustedDependencies` empty or minimal and reviewed; no unvetted postinstall scripts.
- Run before declaring done:
  - `bun audit` → 0
  - `dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive` → 0
- Verify licenses: no commercial/“surprise” licenses. MediatR and AutoMapper are **banned**.

**DO NOT**

- Use `bun add --no-save`, `latest`, `*`, or floating versions.
- Disable `TreatWarningsAsErrors` or NuGet audit to make a build pass.
- Add a package published < 7 days ago (blocked by `minimumReleaseAge`) or bypass it.
- Commit `node_modules/`, `bin/`, `obj/`, `dist/`, `.wrangler/`, `.astro/`, `.env`, `.dev.vars`.

## 3. Provider anonymity

**DO**

- Use only `ProviderUid` (`Guid`) in public URLs (`/[lang]/product/{providerUid}/{id}`), API routes, JSON, DTOs, HTML, OG tags, JSON-LD, `robots.txt`, `sitemap.xml`.
- Keep `InternalKey` inside `Infrastructure` configuration/DI/logs.
- Sanitize upstream error codes to generic `ProblemDetails` (`503 ProviderUnavailable` for auth/freeze/IP issues).

**DO NOT**

- Emit the literal `"shop2topup"` anywhere public. Grep must return 0 outside internal config/infrastructure.
- Emit upstream `error.details` or `action` text.
- Use provider names in CSS classes, alt text, filenames, test snapshots, or commit messages that become public artifacts.

## 4. Secrets

**DO**

- Generate `API_CLIENT_KEY` with ≥ 32 random bytes (`openssl rand -hex 32`).
- Use `dotnet user-secrets` for local backend secrets.
- Use `.env.example` files with placeholders only; real `.env` is gitignored.
- Set file permissions `600` for env files on the Pi (`EnvironmentFile`).
- Rotate immediately if a secret is exposed, then purge history if committed.

**DO NOT**

- Reuse the Valkey password, API client key, or provider secret across environments.
- Commit real values, even in “temporary” branches or test fixtures.
- Print secrets in exception messages or health endpoints.

## 5. API hardening

**DO**

- Require `X-Api-Client-Key` on all `/api/v1/*`; constant-time compare; reject missing/invalid with `401` without detail.
- Rate limit: ~120 req/min per client IP general, ~30 req/min on `/price`, plus a global cap.
- `AddServerHeader = false`; security headers `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.
- Validate every query parameter (`providerUid` as `Guid`, `pageSize` capped, enums parsed strictly).
- CORS from configuration; empty in production (server-to-server).
- `ValidateDataAnnotations().ValidateOnStart()` on all options.
- OpenAPI only in `Development`.
- `GET /health/ready` exposes status only, no secrets/connection strings.

**DO NOT**

- Trust client-supplied IDs for authorization or pricing.
- Return raw exception messages or stack traces.
- Allow unbounded `pageSize`, `search` length, or nested filters.
- Cache or log failed auth attempts with credentials.

## 6. External media and content

**DO**

- Accept only absolute `https://` image URLs with a valid host; reject `http:`, `javascript:`, `data:`, `file:`, protocol-relative and malformed URIs → `null`.
- Render with `loading="lazy"`, `referrerpolicy="no-referrer"`, and a local fallback (`/assets/placeholder-product.svg`).
- Escape all text; no `set:html` with untrusted data.
- Enforce a strict CSP in `src/middleware.ts` (no `unsafe-eval`; explicit `img-src`).

**DO NOT**

- Proxy or inline remote HTML/SVG.
- Allow `unsafe-inline` scripts as a shortcut.
- Load remote fonts/scripts without an explicit CSP allowance.

## 7. Privacy and logging

**DO**

- Log request id, route, status, latency, and rate-limit outcome.
- Redact query strings that could contain player identifiers.
- Keep `player_id` out of analytics and long-term logs.

**DO NOT**

- Log full request bodies, tokens, or raw provider responses containing `unit_price`.
- Send PII to third-party analytics.

## 8. Verification gates

```bash
bun audit
dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive
git grep -in "shop2topup" -- ':!backend/src/VerkkuGames.Infrastructure' ':!backend/src/VerkkuGames.Api/appsettings*' ':!*.md' ':!docs'
git grep -inE "unit_price|original_price" -- ':!backend/src/VerkkuGames.Infrastructure' ':!*.md'
git grep -inE "(KeySecret|API_CLIENT_KEY|VALKEY_PASSWORD)\s*=\s*[^\"'$<{]" -- ':!*.example' ':!*.md'
```

Expected: 0 vulnerabilities, 0 public provider-name matches, 0 reseller-cost leaks, 0 hardcoded secrets.

## Incident checklist

1. Rotate the exposed credential (provider panel / `wrangler secret put` / `dotnet user-secrets`).
2. Invalidate sessions/cache keys if applicable.
3. Purge from git history if committed; force-push only with explicit approval.
4. Document the incident and the blast radius in the task report.
