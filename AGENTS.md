# AGENTS.md — Verkku Games v1

Contrato de operación para agentes de IA que trabajen en este repositorio. Si algo en este archivo contradice una instrucción puntual, **pregunta antes de romper un invariante**.

- **Repo:** `F:\Verkku\local\verkku-games\` (monorepo, git propio)
- **Producto:** tienda digital de juegos, giftcards y recargas para Venezuela/LatAm; ES/EN; región USA/Global/MX/PA/CO; precios USD + BTC/ETH/USDT/USDC.
- **Estado v1:** catálogo de **solo consulta** (CTA "Próximamente"). Sin carrito, checkout ni órdenes.

## Orden de lectura obligatorio

1. `docs/design/2026-11-25-verkku-games-v1-design.md` — fuente de verdad funcional.
2. `.opencode/rules/architecture.md` — límites de capas y contratos del backend.
3. `.opencode/rules/security.md` — cadena de suministro, secretos, frontera BFF.
4. `.opencode/rules/frontend-seo-i18n.md` — islas, i18n, SEO, accesibilidad.
5. El `README.md` del servicio que vas a tocar.

## Mapa del repo

| Ruta | Qué es |
| --- | --- |
| `backend/VerkkuGames.slnx` | Solución .NET 10 (`net10.0`, SDK `10.0.402`) |
| `backend/src/VerkkuGames.Domain` | Value objects y modelos; **cero dependencias** |
| `backend/src/VerkkuGames.Application` | Puertos, queries, pricing, regiones, validadores |
| `backend/src/VerkkuGames.Infrastructure` | SHOP2TOPUP, Binance, HybridCache/Redis |
| `backend/src/VerkkuGames.Api` | Minimal APIs, auth BFF, rate limiting |
| `backend/tests/*` | xUnit + NSubstitute / `WebApplicationFactory` |
| `frontend/` | Astro 7 SSR + islas React 19 + Tailwind v4, Bun 1.4.5 |
| `frontend/src/pages/api/` | BFF del Worker hacia la RPi |
| `docker-compose.yml` | Valkey 8 (+ perfil `api` para la RPi) |
| `.opencode/rules`, `.opencode/agents` | Reglas y perfiles de agente |

## Invariantes no negociables

1. **Cero mocks/fallbacks falsos en runtime.** El catálogo se obtiene de SHOP2TOPUP real (y de Valkey/Redis). Si faltan credenciales o el proveedor falla → error controlado (`ProblemDetails` / estado de error en UI). Los mocks existen **solo** en `backend/tests/**` y en tests de Bun.
2. **Nunca exponer al proveedor mayorista.** La cadena `"shop2topup"` no puede aparecer en URLs públicas, JSON de la API pública, HTML, OpenGraph, JSON-LD, `robots.txt` ni `sitemap.xml`. Toda referencia pública usa el **`ProviderUid` (Guid opaco)**. `InternalKey` solo en configuración, DI y logs internos.
3. **Nunca exponer el costo reseller.** `unit_price` / `original_price` de SHOP2TOPUP, API keys y errores crudos del proveedor jamás salen al cliente. Solo se publica el precio retail calculado.
4. **Dinero = `decimal`.** Prohibido `float`/`double` para dinero o tasas en .NET. Siempre `CultureInfo.InvariantCulture` al parsear/formatear. Monedas: `USD`, `USDT`, `USDC` (2 dec), `ETH` (6 dec), `BTC` (8 dec).
5. **Sin Bs/VES.** No hay precios, conversión ni copy en bolívares. Es decisión de producto.
6. **La Raspberry Pi no se expone.** El navegador solo habla con el Worker; el Worker habla con la API vía Cloudflare Tunnel con `X-Api-Client-Key` y `X-Forwarded-Client-Ip`.
7. **`X-Forwarded-Client-Ip` se confía solo después de autenticar** `X-Api-Client-Key`. Antes de autenticar, la cabecera se ignora y se responde `401`.
8. **CTA v1 = solo lectura.** "Próximamente" / "Coming soon". Sin carrito, checkout, creación de órdenes, ni enlaces de venta a WhatsApp/Telegram.
9. **Sin paquetes de licencia comercial.** No MediatR, no AutoMapper. CQRS liviano propio + mapeo explícito. No agregues dependencias sin justificar licencia, mantenimiento y CVEs.
10. **Verifica antes de declarar terminado.** Builds, tests y auditorías deben pasar de verdad. No hay "debería funcionar".

## Reglas por dominio

| Archivo | Cárgalo cuando… |
| --- | --- |
| `.opencode/rules/architecture.md` | toques `backend/src/**`, agregues un provider, query, DTO o caché |
| `.opencode/rules/security.md` | toques auth, secretos, headers, rate limiting, imágenes externas, CI o dependencias |
| `.opencode/rules/frontend-seo-i18n.md` | toques `frontend/src/**`, copy, rutas, SEO, Tailwind o islas React |

## Perfiles de agente

| Perfil | Úsalo para |
| --- | --- |
| `.opencode/agents/backend-architect.md` | diseño e implementación de Domain/Application/Infrastructure/Api |
| `.opencode/agents/frontend-astro.md` | Astro SSR, islas React, Tailwind, i18n, SEO, Cloudflare |
| `.opencode/agents/security-auditor.md` | auditoría read-only de supply chain, fugas, headers y fronteras |

## Comandos de verificación

```bash
# Backend
dotnet build backend/VerkkuGames.slnx
dotnet test  backend/VerkkuGames.slnx
dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive

# Frontend
cd frontend
bun install --frozen-lockfile
bun test
bun run check
bun run build
bun audit

# Fuga del proveedor (0 resultados esperados)
git grep -in "shop2topup" -- ':!backend/src/VerkkuGames.Infrastructure' ':!backend/src/VerkkuGames.Api/appsettings*' ':!*.md' ':!docs'
```

## Definition of Done

- [ ] `dotnet build` y `dotnet test` en verde; 0 warnings (el repo trata warnings como errores).
- [ ] `dotnet list package --vulnerable --include-transitive` → 0 vulnerabilidades.
- [ ] `bun test`, `bun run check`, `bun run build` en verde.
- [ ] `bun audit` → 0 vulnerabilidades.
- [ ] Grep de `"shop2topup"` y `unit_price` limpio en superficie pública.
- [ ] Sin secretos en el diff (`.env`, `.dev.vars`, keys, tokens).
- [ ] Sin `float`/`double` para dinero; sin Bs/VES.
- [ ] Copy nuevo presente en `es.ts` **y** `en.ts`.
- [ ] Si tocaste rutas o contratos: READMEs y reglas actualizados.
- [ ] Reporte breve de lo hecho, comandos ejecutados y resultados reales.

## Convenciones de trabajo

- **No** ejecutes `git add` / `git commit` salvo que el orquestador lo pida.
- Trabaja dentro de `F:\Verkku\local\verkku-games\`; no toques rutas fuera del repo.
- Cambios mínimos y coherentes con el estilo existente. Nada de refactors no solicitados.
- Si un invariante bloquea la tarea, detente y reporta la contradicción en lugar de improvisar.
- Los `.md` nuevos van en español para documentación de producto y en inglés para reglas/perfiles técnicos.
