# Verkku Games

Tienda digital de **juegos, giftcards y recargas (top-ups)** para Venezuela y LatAm. Permite elegir la **región del producto** (USA, Global, México, Panamá, Colombia) porque la mayoría de tiendas de consolas restringen la compra directa desde Venezuela, muestra **precios en USD** con equivalentes en **BTC / ETH / USDT / USDC** (tasas reales de Binance) y es **bilingüe nativo ES/EN**.

> **Estado v1:** catálogo de **solo consulta**. La ficha de producto muestra precio en vivo, stock, región y requisitos del juego con el CTA **"Próximamente"**. No hay carrito, checkout ni creación de órdenes todavía (ver [Roadmap v2](#roadmap-v2)).

- **Web objetivo:** `https://games.verkku.com`
- **Repositorio:** monorepo con `backend/` (.NET 10) y `frontend/` (Astro 7 en Cloudflare Workers).
- **Marca:** submarca de Verkku para productos digitales y la futura división **Verkku Games Studio**.

---

## 1. Stack

| Capa | Tecnología | Versión pinneada |
| --- | --- | --- |
| Frontend | Astro (SSR, `output: "server"`) | `^7.4.12` |
| Frontend | `@astrojs/cloudflare` (Workers + Static Assets) | `^14.4.8` |
| Frontend | `@astrojs/react` + React | `^7.1.9` / `^19.3.1` |
| Frontend | Tailwind CSS (`@tailwindcss/vite`) | `^4.3.5` |
| Frontend | TypeScript (strict) | `^5.9.3` |
| Frontend | Wrangler | `^4.164.0` |
| Tooling | Bun | `1.4.5` |
| Backend | .NET SDK (`backend/global.json`) | `10.0.402` (`net10.0`) |
| Backend | ASP.NET Core Minimal APIs | `10.0.x` |
| Backend | `Microsoft.Extensions.Caching.Hybrid` | `10.11.1` |
| Backend | `Microsoft.Extensions.Caching.StackExchangeRedis` | `10.0.15` |
| Backend | `Microsoft.Extensions.Http.Resilience` | `10.11.1` |
| Backend | FluentValidation | `12.2.0` |
| Backend | xUnit + NSubstitute | `2.9.3` / `6.2.0` |
| Datos | Valkey (`valkey/valkey:8-alpine`) | 8 |

**Regla de oro de licencias:** cero paquetes con licencia comercial sorpresa. **No se usa MediatR ni AutoMapper**; hay un CQRS liviano propio y mapeo explícito.

---

## 2. Arquitectura

```text
+---------------------------------------------------------------+
|  Navegador (ES / EN)                                          |
|  HTML SSR + islas React 19                                    |
+-------------------------------+-------------------------------+
                                | HTTPS
                                v
+---------------------------------------------------------------+
| Cloudflare Workers (edge)                                     |
|  Astro 7 SSR  +  BFF /api/items / /api/price / /api/rates     |
|  Inyecta  X-Api-Client-Key                                    |
|  Reenvia  X-Forwarded-Client-Ip  (desde CF-Connecting-IP)     |
|  Oculta la URL real de la Raspberry Pi y la API key           |
+-------------------------------+-------------------------------+
                                | HTTPS - Cloudflare Tunnel
                                | (sin abrir puertos)
                                v
+---------------------------------------------------------------+
| Raspberry Pi (ARM64, Linux)                                   |
|  .NET 10 API - Clean Architecture                             |
|  /api/v1/catalog/*   /api/v1/rates   /health/live|ready       |
|  Valkey 8 - HybridCache (L1 RAM + L2 Redis)                   |
+---------------+---------------+-------------------------------+
                |                               |
                v                               v
      SHOP2TOPUP Reseller API v1        Binance Market Data API
      (catalogo, stock, precio)         (BTC/ETH/USDC -> USD, sin key)
```

**Decisiones clave**

1. El navegador **nunca** habla directo con la Raspberry Pi: todo pasa por el BFF de Astro en Cloudflare.
2. El proveedor mayorista se identifica públicamente con un **`Guid` opaco (`ProviderUid`)**. La cadena `"shop2topup"` no aparece en URLs, JSON públicos, HTML ni `sitemap.xml`.
3. El costo reseller (`unit_price` / `original_price`) y las API keys **jamás** se serializan al cliente.
4. El dinero y las tasas usan **`decimal`** (backend) y formateo fijo (frontend). Nunca `float`/`double`.
5. No hay **mocks en runtime**: si el proveedor falla o faltan credenciales, se devuelve un error controlado (ProblemDetails / estado de error en UI). Los mocks viven solo en proyectos de test.

---

## 3. Estructura del repositorio

```text
verkku-games/
├── README.md                  # este archivo
├── AGENTS.md                  # contrato de operación para agentes de IA
├── docker-compose.yml         # Valkey (+ perfil opcional "api" ARM64)
├── .env.example               # variables para docker-compose
├── docs/design/               # diseño y plan aprobados
├── .opencode/
│   ├── rules/                 # architecture.md · security.md · frontend-seo-i18n.md
│   └── agents/                # backend-architect.md · frontend-astro.md · security-auditor.md
├── backend/
│   ├── VerkkuGames.slnx
│   ├── global.json            # SDK 10.0.402
│   ├── Directory.Build.props  # net10.0, nullable, warnings as errors
│   ├── Directory.Packages.props# Central Package Management
│   ├── Dockerfile             # multi-stage ARM64/x64, usuario no-root
│   ├── src/
│   │   ├── VerkkuGames.Domain/         # cero dependencias externas
│   │   ├── VerkkuGames.Application/    # puertos, queries, pricing, regiones
│   │   ├── VerkkuGames.Infrastructure/ # SHOP2TOPUP, Binance, HybridCache
│   │   └── VerkkuGames.Api/            # Minimal APIs, seguridad, rate limiting
│   └── tests/
│       ├── VerkkuGames.UnitTests/
│       └── VerkkuGames.IntegrationTests/
└── frontend/
    ├── bunfig.toml
    ├── astro.config.mjs
    ├── wrangler.jsonc
    ├── public/assets/logo.svg
    └── src/                    # i18n · lib · layouts · components · pages (BFF incluido)
```

Documentación por servicio: [`backend/README.md`](backend/README.md) · [`frontend/README.md`](frontend/README.md).

---

## 4. Prerrequisitos

| Herramienta | Versión | Notas |
| --- | --- | --- |
| .NET SDK | **10.0.402** | `backend/global.json` fija la versión; `rollForward: latestFeature` |
| Bun | **1.4.5+** | Gestor y runtime del frontend (`bun --version`) |
| Docker Desktop / Docker Engine + Compose | reciente | Solo para Valkey local y el perfil `api` |
| Git | reciente | — |
| (Producción) Cloudflare account + `cloudflared` | — | Workers, secretos y Tunnel hacia la RPi |

No se necesita cuenta ni API key de Binance: se usa el endpoint público `https://data-api.binance.vision`.

---

## 5. Credenciales requeridas (checklist)

Todo lo que hay que conseguir **antes de correr en vivo**. Ningún valor real debe commitearse.

### 5.1 Servicios externos

| # | Variable | Consumidor | Cómo obtenerla | ¿Bloquea el arranque en vivo? |
| --- | --- | --- | --- | --- |
| 1 | `Shop2TopUp__KeyId` | Backend | Panel Reseller de SHOP2TOPUP → **API Access** → Key ID | **Sí** |
| 2 | `Shop2TopUp__KeySecret` | Backend | Panel Reseller de SHOP2TOPUP → **API Access** → Key Secret | **Sí** |
| 3 | `ApiSecurity__ClientApiKey` (backend) / `API_CLIENT_KEY` (frontend, docker) | Backend + BFF | Generar 32 bytes aleatorios, p. ej. `openssl rand -hex 32`. **El mismo valor en ambos lados.** | **Sí** |
| 4 | `VALKEY_PASSWORD` | Docker / Backend | Generar aleatorio. En dev, el compose usa `change_me_local_dev_only` | Sí en producción |
| 5 | `PUBLIC_SITE_URL` | Frontend (SEO + build) | URL pública final, ej. `https://games.verkku.com` | **Sí** |
| 6 | `API_BASE_URL` | Frontend (solo servidor) | URL HTTPS del Cloudflare Tunnel hacia la RPi, ej. `https://api.games.verkku.com` | **Sí** |

> `Shop2TopUp__ProviderUid` tiene default determinista `d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42`. Cámbialo solo si necesitas invalidar URLs públicas ya indexadas.

### 5.2 Secretos de Cloudflare Workers

```bash
cd frontend
bunx wrangler login
bunx wrangler secret put API_CLIENT_KEY     # mismo valor que ApiSecurity__ClientApiKey
```

`API_BASE_URL` y `PUBLIC_SITE_URL` se declaran como `vars` en `frontend/wrangler.jsonc` (no son secretos, pero **nunca** deben llevar el prefijo `PUBLIC_` salvo `PUBLIC_SITE_URL`, que es intencionalmente público).

### 5.3 Requisitos operativos de SHOP2TOPUP

- **IP allowlist:** la IP pública de salida de la Raspberry Pi (o del host que corre la API) debe estar autorizada en el panel reseller. Si no, la API responde `IP_NOT_ALLOWED` y el cliente recibe un `503` genérico.
- **Recarga de cuenta (`RELOAD_REQUIRED` / `prices_locked`):** si el saldo reseller se agota, el proveedor bloquea precios (`prices_locked: true` en listados y `403 RELOAD_REQUIRED` en precio en vivo). Hay que **recargar la cuenta en el panel SHOP2TOPUP**; no es un bug del código. El catálogo sigue listándose con `RetailPriceUsd = null`.
- **Cuota diaria:** `POST /player/validate` consume cuota. v1 **no** lo llama; solo muestra los requisitos que pedirá el juego.

### 5.4 Variables opcionales (tuning)

| Variable | Default | Rango |
| --- | --- | --- |
| `Pricing__DefaultMarkupPercent` / `PRICING_DEFAULT_MARKUP_PERCENT` | `10.0` | `0..500` |
| `Pricing__MinimumMarginUsd` / `PRICING_MINIMUM_MARGIN_USD` | `0.25` | `0..1000` |
| `Shop2TopUp__BaseUrl` | `https://shop2topup.com/api/endpoints/v1` | — |
| `Binance__BaseUrl` | `https://data-api.binance.vision` | — |
| `Cache__KeyPrefix` | `verkkugames:v1:` | — |

---

## 6. Quickstart local

### 6.1 Valkey (Redis-compatible)

```bash
# desde la raíz del repo
cp .env.example .env          # ajusta VALKEY_PASSWORD si quieres
docker compose up -d valkey
docker compose ps             # verkku-games-valkey debe estar "healthy"
```

### 6.2 Backend (.NET 10)

```bash
cd backend
dotnet restore VerkkuGames.slnx
dotnet run --project src/VerkkuGames.Api
# API en http://localhost:5180  ·  health: GET /health/live
```

Credenciales locales con **User Secrets** (no se escriben en disco del repo):

```bash
cd backend/src/VerkkuGames.Api
dotnet user-secrets set "Shop2TopUp:KeyId" "tu_key_id"
dotnet user-secrets set "Shop2TopUp:KeySecret" "tu_key_secret"
dotnet user-secrets set "ApiSecurity:ClientApiKey" "change_me_local_dev_bff_key_32bytes"
dotnet user-secrets set "Cache:RedisConnectionString" "localhost:6379,password=change_me_local_dev_only,abortConnect=false"
```

### 6.3 Frontend (Astro 7 + Bun)

```bash
cd frontend
bun install
cp .env.example .env          # PUBLIC_SITE_URL, API_BASE_URL, API_CLIENT_KEY
bun run dev                   # http://localhost:4321 → redirige a /es/
```

> El BFF (`/api/*`) requiere `API_CLIENT_KEY` también en el frontend; debe coincidir con `ApiSecurity__ClientApiKey` del backend.

---

## 7. Verificación y auditorías de seguridad

Ejecuta **todo** esto antes de decir que algo está terminado:

```bash
# Backend
dotnet build backend/VerkkuGames.slnx
dotnet test  backend/VerkkuGames.slnx
dotnet list backend/VerkkuGames.slnx package --vulnerable --include-transitive

# Frontend
cd frontend
bun install --frozen-lockfile
bun test
bun run check          # astro check (TypeScript + .astro)
bun run build          # build de producción para Workers
bun audit              # 0 vulnerabilidades esperado
```

**Chequeo de fuga del proveedor** (debe devolver 0 coincidencias fuera del código interno/config):

```bash
git grep -in "shop2topup" -- ':!backend/src/VerkkuGames.Infrastructure' ':!backend/src/VerkkuGames.Api/appsettings*' ':!*.md' ':!docs'
```

Las auditorías actuales del proyecto: **0 vulnerabilidades NuGet** y **0 vulnerabilidades Bun** (Astro 7 eliminó los 49 CVEs de Astro 5).

---

## 8. Despliegue

### Frontend — Cloudflare Workers

```bash
cd frontend
bun install --frozen-lockfile
bun run build
bunx wrangler deploy
bunx wrangler secret put API_CLIENT_KEY
```

- `wrangler.jsonc` usa **Workers with Static Assets** (`main` + `assets.binding = ASSETS`), `compatibility_date` y `nodejs_compat`.
- El worker necesita `API_BASE_URL` apuntando al túnel y `PUBLIC_SITE_URL` para canonical/hreflang/robots/sitemap.

### Backend — Raspberry Pi ARM64

**Opción A — Docker Compose (recomendada):**

```bash
# en la RPi, dentro del repo
docker compose --profile api up -d --build
# API escuchando solo en 127.0.0.1:8080
```

**Opción B — binario nativo:**

```bash
dotnet publish backend/src/VerkkuGames.Api -c Release -r linux-arm64 --self-contained false -o ./publish
```

Expón la API **únicamente** a través de Cloudflare Tunnel (`cloudflared tunnel run ...` → `http://127.0.0.1:8080`). No abras puertos en el router ni publiques la IP de la RPi.

### Orden recomendado

1. Valkey arriba y `healthy`.
2. API arriba y `GET /health/ready` en `200`.
3. Túnel de Cloudflare validado con `curl` + `X-Api-Client-Key`.
4. `wrangler deploy` del frontend y smoke test de `/es/`, `/en/`, `/es/catalog`, ficha de producto y `/sitemap.xml`.

---

## 9. Roadmap v2

| Área | Alcance |
| --- | --- |
| Persistencia | **PostgreSQL** como store transaccional (usuarios, órdenes, auditoría de pagos) |
| Compra | Carrito, checkout, creación real de órdenes en el proveedor (`POST /orders/create`), estados y reintentos |
| Validación | `POST /player/validate` con control de cuota diaria y caché de resultados |
| Mensajería | Colas/outbox para órdenes y webhooks, workers idempotentes |
| Notificaciones | Email/Telegram/WhatsApp transaccional (solo post-compra; no como canal de venta v1) |
| Operación | Panel administrativo, métricas de margen, alertas de saldo reseller |
| Contenido | CMS para **Verkku Games Studio** y fichas editoriales |

Los pagos siguen en USD con referencia cripto; **no se agrega Bs/VES** (decisión del producto).

---

## 10. Documentación y gobierno

- [`AGENTS.md`](AGENTS.md) — invariantes no negociables y comandos de verificación para agentes de IA.
- [`.opencode/rules/architecture.md`](.opencode/rules/architecture.md)
- [`.opencode/rules/security.md`](.opencode/rules/security.md)
- [`.opencode/rules/frontend-seo-i18n.md`](.opencode/rules/frontend-seo-i18n.md)
- [`.opencode/agents/backend-architect.md`](.opencode/agents/backend-architect.md)
- [`.opencode/agents/frontend-astro.md`](.opencode/agents/frontend-astro.md)
- [`.opencode/agents/security-auditor.md`](.opencode/agents/security-auditor.md)
- Diseño aprobado: [`docs/design/2026-11-25-verkku-games-v1-design.md`](docs/design/2026-11-25-verkku-games-v1-design.md)

© Verkku. Código propietario; no redistribuir sin autorización.
