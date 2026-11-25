# Verkku Games — Diseño de Arquitectura v1

- **Fecha:** 2026-11-25
- **Estado:** Listo para aprobación del usuario (revisado por builder-check)
- **Ubicación del proyecto:** `F:\Verkku\local\verkku-games\` (monorepo)
- **Marca:** **Verkku Games** (submarca de Verkku para venta de productos digitales y futura división de desarrollo de juegos)

---

## 1. Propósito y Visión

Construir la base técnica limpia, segura y escalable de **Verkku Games**, una tienda digital de juegos, giftcards y recargas (top-ups) enfocada inicialmente en el mercado venezolano y latinoamericano.

Debido a que la mayoría de tiendas de consolas y plataformas restringen compras directas desde Venezuela, un pilar funcional desde el día uno es permitir **seleccionar la región del producto** (principales: **USA, Global, México, Panamá y Colombia**) al navegar y escoger cualquier producto.

El proyecto se construye de forma incremental pero **para quedarse** (principio YAGNI sin código desechable): cada capa de v1 será parte del MVP de lanzamiento y quedará preparada para sumar PostgreSQL (transacciones/órdenes), colas, webhooks y notificaciones en v2 sin reescribir el núcleo.

---

## 2. Objetivos, No-Objetivos y Anti-Objetivos

### Objetivos (v1)
1. **Backend .NET 10 con Clean Architecture** listo para desplegar en Raspberry Pi local (ARM64/Linux) con abstracciones limpias para múltiples proveedores de catálogo (`ICatalogProvider`), registro por UID opaco (`ICatalogProviderRegistry`), tasas de cambio (`IExchangeRateProvider`), precios (`IPricingService`), clasificación regional (`IRegionResolver`) y caché (`ICacheService`).
2. **Integración real con SHOP2TOPUP Reseller API v1** para consumir árbol de catálogo (big categories, categories, subcategories/items), disponibilidad, requisitos dinámicos de recarga y precio en vivo.
3. **Precios en USD (`$`) + equivalentes en Cripto** (`BTC`, `ETH`, `USDT`, `USDC`) consumiendo tasas reales de **Binance Market Data API** (`https://data-api.binance.vision`).
4. **Caché distribuida con Redis/Valkey** (vía `.NET 10 HybridCache`: L1 memoria + L2 Redis) para minimizar llamadas a proveedores, evitar *cache stampede* y mantener baja huella de RAM en la Raspberry Pi.
5. **Frontend Astro + React (TypeScript) con Bun** desplegable en **Cloudflare Workers (with Static Assets)** (SSR en el edge), con **i18n nativo ES/EN**, selector de región y moneda, identidad visual Verkku adaptada a un tono gamer natural y **SEO completo** (`robots.txt`, `sitemap.xml`, `canonical`, `hreflang`, OpenGraph, JSON-LD).
6. **Seguridad desde el diseño** en cadena de suministro (Bun hardening, NuGet auditing), fronteras de red (BFF en Cloudflare hacia la Raspberry Pi), ocultamiento total del proveedor mayorista tras un UID opaco, rate limiting por usuario final y manejo seguro de secretos y errores.
7. **Documentación y gobierno del repositorio:** `README.md` raíz y por servicio, `AGENTS.md`, reglas técnicas (`rules/`) y perfiles de agentes del proyecto.

### No-Objetivos (quedan para v2+)
- Carrito de compras, checkout, pasarelas de pago o creación real de órdenes en SHOP2TOPUP (`POST /orders/create`).
- Validación interactiva de `player_id` contra el proveedor (`POST /player/validate` consume cuota diaria; en v1 solo mostramos los requisitos que pedirá el juego).
- Base de datos transaccional (**PostgreSQL**) — entra en v2 cuando existan usuarios, órdenes y auditoría de pagos.
- Colas de mensajes, workers de webhooks, correos/notificaciones y panel administrativo.
- Precios o conversión en **Bolívares (Bs/VES)** (descartado explícitamente por el usuario).
- Canales manuales de venta (WhatsApp/Telegram): en v1 la ficha de producto es **solo consulta / "Próximamente"** (decisión explícita del usuario).
- CMS para la futura sección de desarrollo de juegos (en v1 solo habrá una página ligera de presentación *Verkku Games Studio — Próximamente*).

### Anti-Objetivos (lo que se considera fallo aunque compile)
- **Cero datos falsos o fallbacks mock en tiempo de ejecución** para el catálogo: en runtime se consulta exclusivamente al proveedor real (y Redis). Si el proveedor falla o no hay credenciales válidas, se devuelve un error controlado y transparente (ProblemDetails / estado de error claro en UI). Los mocks existen únicamente en proyectos de tests.
- **Nunca exponer el nombre del proveedor mayorista (`"shop2topup"`)** en URLs públicas, respuestas JSON de la API pública, HTML, ni `sitemap.xml`. Toda referencia pública usa el **UID opaco** del proveedor.
- **Nunca usar `float` o `double` para dinero o tasas:** solo `decimal` con `CultureInfo.InvariantCulture`.
- **Nunca exponer al cliente el costo reseller (`unit_price` de SHOP2TOPUP)**, API keys, ni mensajes de error crudos del proveedor.
- **Nunca exponer la API de la Raspberry Pi directamente al navegador** sin pasar por el BFF/SSR de Cloudflare.
- **Cero paquetes con licencia comercial sorpresa** (no usar MediatR ni AutoMapper; usar abstracciones propias livianas y mapeo explícito).

---

## 3. Identidad de Marca, Assets y Experiencia de Usuario (UX)

Basado en el Manual de Marca de **Verkku** (`Verkku .pdf`), adaptado a **Verkku Games**:

- **Logo oficial:** se copiará `C:\Users\Saydenier\Pictures\logoverkkuformatos\SVG\verkku-logo-notagline.svg` (`viewBox="0 0 775.62 222.49"`, colores `#25c877`, `#209959`, `#4d4d4d`) a `frontend/public/assets/logo.svg` (renombrado estrictamente como `logo.svg`), acompañado en la navbar de la insignia/etiqueta `GAMES`. Se generará un `favicon.svg` minimalista basado en la paleta oficial y un placeholder SVG limpio para productos sin imagen.
- **Paleta oficial (mantenida):**
  - Fondo oscuro principal (default gamer): `#05342E` / `#06352F`
  - Superficies elevadas / cards oscuras: `#0A453D` / `#0E4F46` (derivadas del primario para contraste WCAG AA)
  - Verde marca / CTAs primarios: `#1A8154` / `#209959` (hover `#1F9087`)
  - Acentos luminosos / badges / highlights: `#25C877`, `#81CB79` y `#8BCCAB`
  - Teal secundario: `#2E6D6D`
  - Superficies claras / contraste invertido: `#FBF7F5` y `#EFF0E9`
- **Tipografía:** `Bricolage Grotesque` (titulares) + `Poppins` (cuerpo e interfaz), cargadas con `display=swap` y fallback de sistema.
- **Voz y tono:** natural, cercano y gamer, sin perder la claridad, honestidad técnica y transparencia de Verkku.
- **Idiomas nativos (i18n):**
  - Español (`/es/...`, idioma por defecto para público de Venezuela/LatAm).
  - Inglés (`/en/...`).
  - La ruta raíz `/` redirige (`302`) según cookie de preferencia o cabecera `Accept-Language`, con fallback a `/es/`.
  - Diccionarios tipados en TypeScript (`src/i18n/es.ts` y `src/i18n/en.ts`).
- **Regiones principales:**
  - `USA` (Estados Unidos), `GLOBAL` (Global / Mundial), `MX` (México), `PA` (Panamá), `CO` (Colombia), más `OTHER` (Otras regiones / sin clasificar) para no ocultar productos válidos del proveedor.
- **Monedas mostradas:**
  - Referencia principal siempre en **USD (`$`)** (2 decimales).
  - Selector interactivo persistido en `localStorage` para mostrar equivalentes en **USDT** (2 dec), **USDC** (2 dec), **BTC** (8 dec) o **ETH** (6 dec), manteniendo USD visible como referencia.
- **CTA en ficha de producto (v1):**
  - Estado informativo limpio **"Próximamente / Coming soon"** (sin botón falso de compra ni desvío a WhatsApp/Telegram), permitiendo al visitante consultar precio actualizado en vivo, equivalentes cripto, stock, región y requisitos del juego.

---

## 4. Enfoques Considerados

### Opción A — Monorepo + Astro SSR en Cloudflare Workers + .NET 10 Clean Architecture + Redis *(Elegida)*
- **Descripción:** Frontend Astro 5 (modo `server` con `@astrojs/cloudflare` para Cloudflare Workers with Static Assets) renderiza en el edge llamando al backend .NET 10 (en Raspberry Pi) servidor-a-servidor mediante Cloudflare Tunnel y una clave de cliente interna. Islas React únicamente para interactividad. Backend con Clean Architecture + `HybridCache` (L1 + Redis L2).
- **Trade-offs:** Máximo SEO real en catálogo y productos, mejor seguridad (el navegador nunca habla directo con la Raspberry Pi), arquitectura limpia lista para PostgreSQL y colas en v2. Requiere configurar el túnel/URL de la API en Cloudflare.

### Opción B — Astro Estático (SSG) + Islas React llamando directo a la Raspberry Pi
- **Trade-offs:** Despliegue más simple, pero sacrifica SEO en páginas dinámicas de productos y expone la Raspberry Pi directamente al tráfico del navegador.

### Opción C — Igual que A pero con PostgreSQL + Worker de sincronización desde v1
- **Trade-offs:** Control editorial inmediato, pero rompe YAGNI en v1 y suma carga innecesaria en la Raspberry Pi antes de tener transacciones.

**Decisión:** El usuario seleccionó **Opción A**.

---

## 5. Arquitectura del Backend (.NET 10 — Clean Architecture)

### 5.1 Estructura de Solución (`backend/`)

```text
backend/
├── Directory.Build.props              # Nullable enable, TreatWarningsAsErrors, .NET 10, analyzers
├── Directory.Packages.props           # Central Package Management (versiones pineadas)
├── Dockerfile                         # Multi-stage ARM64/x64 minimal image (usuario no-root)
├── VerkkuGames.slnx                   # Solución moderna .NET 10
├── src/
│   ├── VerkkuGames.Domain/            # Cero dependencias externas
│   ├── VerkkuGames.Application/       # Puertos, casos de uso, validadores, pricing, regiones
│   ├── VerkkuGames.Infrastructure/    # SHOP2TOPUP, Binance, HybridCache/Redis, resiliencia HTTP
│   └── VerkkuGames.Api/               # Minimal APIs v1, seguridad, rate limiting, OpenAPI, health
└── tests/
    ├── VerkkuGames.UnitTests/         # xUnit + NSubstitute/fakes: Domain, Application, Validators, Parsers
    └── VerkkuGames.IntegrationTests/  # WebApplicationFactory + HttpMessageHandler fakes + InMemory/Redis
```

### 5.2 Identificación Opaca de Proveedores (UID)
- Cada proveedor implementa `ICatalogProvider` exponiendo:
  - `string InternalKey` (ej. `"shop2topup"`, uso exclusivamente interno en logs y configuración).
  - `Guid ProviderUid` (UID público estable y opaco configurado vía `Shop2TopUpOptions:ProviderUid`, con default determinista `d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42`).
- `ICatalogProviderRegistry` resuelve de forma bidireccional `ProviderUid -> ICatalogProvider` y lista los proveedores activos por su `ProviderUid`.
- Todas las rutas públicas de la API (`/api/v1/...`), las respuestas JSON y las URLs del frontend (`/[lang]/category/{providerUid}/{id}`, `/[lang]/product/{providerUid}/{id}`) usan **exclusivamente** `ProviderUid` (validado como `Guid` válido).

### 5.3 Capa Domain (`VerkkuGames.Domain`)
- **Value Objects:**
  - `Money(decimal Amount, CurrencyCode Currency)` — inmutable, validación `Amount >= 0`, redondeo comercial (`MidpointRounding.AwayFromZero`) según moneda (`USD/USDT/USDC: 2`, `ETH: 6`, `BTC: 8`).
  - `CurrencyCode` (`USD`, `USDT`, `USDC`, `BTC`, `ETH`).
  - `RegionCode` (`USA`, `GLOBAL`, `MX`, `PA`, `CO`, `OTHER`).
  - `ProductType` (`TopUp`, `Voucher`).
- **Modelos de Catálogo normalizados:**
  - `CatalogBigCategory(Guid ProviderUid, string ExternalId, string Name, string Description, string? ImageUrl)`
  - `CatalogCategory(Guid ProviderUid, string ExternalId, string BigCategoryId, string Name, string Description, string? ImageUrl, RegionCode Region)`
  - `CatalogItem(Guid ProviderUid, string ExternalId, string CategoryId, string CategoryName, string Name, string Description, string? ImageUrl, ProductType ProductType, RegionCode Region, Money? RetailPriceUsd, bool InStock, bool PricesLocked)`
  - `CatalogItemDetail(CatalogItem Item, IReadOnlyList<CategoryRequirement> Requirements, IReadOnlyList<CatalogItem> SiblingItems, IReadOnlyList<CatalogCategory> RelatedRegionCategories)`
  - `ItemPriceQuote(Guid ProviderUid, string ExternalItemId, string ItemName, Money RetailPriceUsd, bool InStock, DateTimeOffset QuotedAtUtc)`
  - `CategoryRequirement(string FieldName, RequirementDataType DataType, string? Placeholder, IReadOnlyList<string> SelectOptions, int? MinLength, int? MaxLength)`
  - `ExchangeRatesSnapshot(CurrencyCode BaseCurrency, IReadOnlyDictionary<CurrencyCode, decimal> RatesPerUsd, DateTimeOffset RetrievedAtUtc)` *(sin exponer detalles internos innecesarios en respuesta pública).*
- **Errores tipados (`Result<T>` / `Error`):**
  - `Error(string Code, string Message, ErrorType Type, int? RetryAfterSeconds = null)` con `ErrorType`: `Validation`, `NotFound`, `Unauthorized`, `Forbidden`, `RateLimited`, `Conflict`, `ProviderUnavailable`, `PricesLocked`.

### 5.4 Capa Application (`VerkkuGames.Application`)
- **Abstracciones / Puertos:**
  - `ICatalogProvider` (`InternalKey`, `ProviderUid`, `GetBigCategoriesAsync`, `GetCategoriesAsync`, `GetItemsAsync`, `GetItemPriceAsync`, `GetCategoryRequirementsAsync`, `CheckHealthAsync`).
  - `ICatalogProviderRegistry` (`GetByUid(Guid providerUid)`, `GetDefault()`, `GetAll()`).
  - `IExchangeRateProvider` (`GetCryptoRatesAsync`).
  - `ICacheService` (`GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, TimeSpan ttl, CancellationToken ct)`).
  - `IPricingService` (`CalculateRetailPrice(decimal providerUnitCostUsd, string? categoryId = null) -> Money`).
  - `IRegionResolver` (`ResolveRegion(string categoryName, string? itemName = null) -> RegionCode`, `ExtractBaseTitle(string categoryName) -> string`).
  - `IQueryHandler<TQuery, TResult>` (CQRS liviano propio, sin MediatR).
- **Casos de uso (Queries) y Validadores (FluentValidation):**
  - `GetBigCategoriesQuery(Guid? ProviderUid)`
  - `GetCategoriesQuery(Guid? ProviderUid, string? BigCategoryId, RegionCode? Region, bool IncludeGlobal, string? Search)`
  - `GetCatalogItemsQuery(Guid? ProviderUid, string? CategoryId, RegionCode? Region, bool IncludeGlobal, ProductType? ProductType, bool InStockOnly, string? Search, int Page, int PageSize)`
  - `GetCatalogItemDetailQuery(Guid ProviderUid, string ItemId)` *(resuelve ítem + hereda `ImageUrl` de la categoría si el ítem no tiene + carga requisitos de la categoría + ítems hermanos de la misma categoría + categorías hermanas en otras regiones con el mismo título base normalizado).*
  - `GetItemPriceQuery(Guid ProviderUid, string ItemId)`
  - `GetCategoryRequirementsQuery(Guid ProviderUid, string CategoryId)`
  - `GetExchangeRatesQuery()`
- **Reglas exactas de Pricing (`IPricingService` — decisión técnica adoptada):**
  - Configuración `PricingOptions`: `DefaultMarkupPercent` (default `10.0m`, rango `0..500`), `MinimumMarginUsd` (default `0.25m`, rango `0..1000`), `CategoryMarkupOverrides` (diccionario opcional `categoryId -> percent`).
  - Fórmula: `MinimumMarginUsd` actúa como **piso (floor)**, no como cargo aditivo:
    - `effectivePercent = CategoryMarkupOverrides.TryGetValue(categoryId, out var ov) ? ov : DefaultMarkupPercent`
    - Si `effectivePercent == 0m`, `retailUsd = cost` (permite promociones explícitas a 0% sin forzar piso).
    - Si `effectivePercent > 0m`, `retailUsd = Math.Max(cost * (1m + effectivePercent / 100m), cost + MinimumMarginUsd)`.
    - Redondeo final con `Math.Round(retailUsd, 2, MidpointRounding.AwayFromZero)`.
  - El costo reseller (`unit_price`) jamás se serializa en respuestas públicas.
- **Reglas exactas de Resolución y Filtrado de Regiones (`IRegionResolver` — decisión técnica adoptada):**
  - Opera **exclusivamente** sobre `categoryName` y `itemName` (sin llamar a `/requirements` durante listados, evitando fan-out N+1 y garantizando consistencia entre listado y detalle).
  - Coincidencia determinista (case-insensitive con límites de palabra):
    - `USA`: `\b(usa|us|united states|eeuu)\b`
    - `MX`: `\b(mexico|méxico|mx)\b`
    - `PA`: `\b(panama|panamá|pa)\b`
    - `CO`: `\b(colombia|co)\b`
    - `GLOBAL`: `\b(global|worldwide|ww|international|all regions)\b`
    - En otro caso: `OTHER`.
  - Filtrado por región en API: coincidencia exacta por defecto (`includeGlobal=false`); cuando `includeGlobal=true` y `region` es un país (`USA`, `MX`, `PA`, `CO`), incluye también productos `GLOBAL` (en el frontend el toggle *"Incluir Global"* vendrá activo por defecto al elegir país, con badge visible `"Global"`).
  - `ExtractBaseTitle` elimina sufijos/tokens de región (`USA`, `Global`, `Mexico`, `MX`, `Panama`, `Colombia`, paréntesis vacíos) para enlazar categorías hermanas multi-región en la página de detalle.

### 5.5 Capa Infrastructure (`VerkkuGames.Infrastructure`)
- **Contrato verificado de SHOP2TOPUP Reseller API v1 (`https://shop2topup.com/api/endpoints/v1`):**
  - Auth: `Authorization: Bearer {KeyId}.{KeySecret}` desde `Shop2TopUpOptions` (env vars / User Secrets).
  - `GET /catalog/big-categories?for_ui=true` → `{ "success": true, "big_categories": [{ "id": 1, "name": "...", "description": "...", "image"?: "..." }] }`
  - `GET /catalog/categories?for_ui=true[&bigCategoryId={id}]` (`bigCategoryId` es **opcional**) → `{ "success": true, "categories": [{ "id": 12, "name": "...", "description": "...", "big_category_id": 1, "big_category_name": "...", "image"?: "..." }] }`
  - `GET /catalog/subcategories[?categoryId={id}]` (`categoryId` es **opcional**) → `{ "success": true, "subcategories": [{ "item_id": 999, "name": "...", "description": "...", "category_id": 12, "category_name": "...", "price"?: "0.99", "prices_locked"?: true, "in_stock": true, "product_type": "topup"|"voucher", "image"?: "..." }] }`
  - `GET /catalog/subcategory/{itemId}/price` → `{ "success": true, "price": { "item_id": 999, "item_name": "...", "unit_price": "0.950000", "currency": "USD", "original_price": "0.990000", "discount_applied": true, "in_stock": true, "timestamp": "..." } }`
  - `GET /catalog/category/{categoryId}/requirements` → `{ "success": true, "requirements": [{ "field_name": "player_id", "data_type": "text"|"number"|"single_select"|"multi_select", "placeholder": "...", "select_options"?: [...], "min_length"?: ..., "max_length"?: ... }] }`
  - `GET /account` → solo para readiness check interno (cacheado 5 min, nunca expuesto).
  - Error envelope: `{ "success": false, "error": { "code": "...", "message": "...", "action": "...", "retryable": bool, "retry_after": int|null, "details": {...} } }`.
  - Mapeo explícito de códigos:
    - `RELOAD_REQUIRED` (403) y `prices_locked: true` → `PricesLocked` / `RetailPriceUsd = null` con `PricesLocked = true` sin romper el listado.
    - `NO_REQUIREMENTS_FOUND` (404) → lista vacía de requisitos.
    - `ITEM_NOT_FOUND` (404) → `ErrorType.NotFound`.
    - `RATE_LIMIT_EXCEEDED` (429) → `ErrorType.RateLimited` con `Retry-After`.
    - `PRICING_UPDATING` (409) → `ErrorType.Conflict` con `Retry-After`.
    - `MISSING_API_KEY` / `INVALID_API_KEY` / `IP_NOT_ALLOWED` / `ACCOUNT_FROZEN` → log estructurado `Error` en servidor, respuesta genérica `503 ProviderUnavailable` al cliente.
  - Resiliencia HTTP (`Microsoft.Extensions.Http.Resilience`): timeout 8s por intento, retry exponencial con jitter solo para `5xx`/`408`/red (nunca `400/401/403/404`), y circuit breaker.
  - Herencia de imagen: al armar `CatalogItem`, si la subcategoría no incluye imagen propia, hereda `ImageUrl` de su `CatalogCategory` cacheada.
- **Integración Binance (`BinanceExchangeRateProvider`):**
  - Endpoint oficial sin geo-bloqueo ni API key: `GET https://data-api.binance.vision/api/v3/ticker/price?symbols=%5B%22BTCUSDT%22,%22ETHUSDT%22,%22USDCUSDT%22%5D`.
  - Tasas por USD: `USD = 1m`, `USDT = 1m`, `USDC = 1m / usdcUsdt`, `BTC = 1m / btcUsdt`, `ETH = 1m / ethUsdt`.
- **Caché (`RedisHybridCacheService`):**
  - `.NET 10 HybridCache` (L1 memoria + L2 Redis vía `Microsoft.Extensions.Caching.StackExchangeRedis`).
  - TTLs configurables: Big categories `30m`, Categories `15m`, Items `10m`, Requirements `30m`, Live price `60s`, Crypto rates `60s`.
  - Prefijo: `verkkugames:v1:...`.
  - `docker-compose.yml` usa `valkey/valkey:8-alpine` (compatible 100% protocolo Redis, ARM64/x64, `maxmemory 128mb`, `allkeys-lru`, sin exponer puerto público en producción).

### 5.6 Capa API (`VerkkuGames.Api`)
- **Endpoints REST (`/api/v1`):**
  - `GET /api/v1/catalog/big-categories?providerUid=`
  - `GET /api/v1/catalog/categories?providerUid=&bigCategoryId=&region=&includeGlobal=false&search=`
  - `GET /api/v1/catalog/items?providerUid=&categoryId=&region=&includeGlobal=false&productType=&inStockOnly=false&search=&page=1&pageSize=24`
  - `GET /api/v1/catalog/items/{providerUid:guid}/{itemId}` *(detalle completo + requisitos + hermanos de categoría + categorías hermanas multi-región)*
  - `GET /api/v1/catalog/items/{providerUid:guid}/{itemId}/price`
  - `GET /api/v1/catalog/categories/{providerUid:guid}/{categoryId}/requirements`
  - `GET /api/v1/rates`
  - `GET /health/live` (liveness)
  - `GET /health/ready` (readiness Redis + configuración, sin datos sensibles)
- **Seguridad y Rate Limiting (decisión técnica adoptada):**
  - Autenticación BFF obligatoria en `/api/v1/*`: valida `X-Api-Client-Key` con `CryptographicOperations.FixedTimeEquals`.
  - **Rate limiting particionado por visitante real detrás de Cloudflare:** el BFF de Astro reenvía la IP real del visitante en `X-Forwarded-Client-Ip` (obtenida de `CF-Connecting-IP`). ASP.NET Core confía en `X-Forwarded-Client-Ip` **solo después** de autenticar `X-Api-Client-Key`, aplicando límites particionados por IP final (ej. 120 req/min general, 30 req/min en `/price`) más un tope global de protección. Así un usuario abusivo recibe `429` sin afectar a los demás visitantes.
  - CORS estricto desde configuración (vacío por defecto en producción), `AddServerHeader = false`, cabeceras `nosniff`/`DENY`/`no-referrer`, `ProblemDetails` RFC 9457, `ValidateDataAnnotations().ValidateOnStart()` en todas las opciones y OpenAPI habilitado solo en `Development`.

---

## 6. Arquitectura del Frontend (Astro + React TS + Bun en Cloudflare Workers)

### 6.1 Stack, Despliegue y Seguridad de Dependencias (`frontend/`)
- **Runtime y gestor:** **Bun `1.4.5`** (se actualizará antes de crear el proyecto).
- **Hardening en `bunfig.toml`:** `minimumReleaseAge = 604800` (7 días), lockfile estricto (`--frozen-lockfile`), `trustedDependencies` vacío o mínimo estrictamente auditado, verificación obligatoria con `bun audit`.
- **Framework y despliegue:** **Astro 5** (`output: "server"`, `@astrojs/cloudflare` apuntando a **Cloudflare Workers with Static Assets** vía `wrangler.jsonc` con `main` y `assets`) + **React 19** (`@astrojs/react`) + **TypeScript strict** + **Tailwind CSS v4** (`@tailwindcss/vite`).

### 6.2 Estructura de Rutas y BFF

```text
frontend/
├── bunfig.toml
├── wrangler.jsonc                     # Cloudflare Workers with Static Assets
├── astro.config.mjs                   # output: 'server', cloudflare adapter, i18n, site
├── public/
│   ├── assets/
│   │   ├── logo.svg                   # Logo oficial Verkku copiado y renombrado a logo.svg
│   │   └── placeholder-product.svg    # Placeholder limpio con paleta Verkku
│   ├── favicon.svg
│   └── site.webmanifest
└── src/
    ├── middleware.ts                  # Cabeceras de seguridad (CSP, HSTS, X-Frame-Options, etc.)
    ├── i18n/
    │   ├── config.ts                  # locales: ['es', 'en'], defaultLocale: 'es'
    │   ├── es.ts                      # Copy gamer natural en español
    │   ├── en.ts                      # Copy gamer natural en inglés
    │   └── utils.ts                   # Traductor tipado + helpers hreflang
    ├── lib/
    │   ├── api-client.ts              # SOLO servidor: llama a RPi con X-Api-Client-Key + X-Forwarded-Client-Ip
    │   ├── currency.ts                # Formateo seguro USD/USDT/USDC/BTC/ETH
    │   ├── regions.ts                 # Metadatos y etiquetas ES/EN de regiones (USA, GLOBAL, MX, PA, CO, OTHER)
    │   └── seo.ts                     # Meta, OpenGraph, JSON-LD (WebSite, Organization, Product, Breadcrumb)
    ├── layouts/
    │   └── BaseLayout.astro           # Head SEO completo, hreflang, canonical, JSON-LD, Navbar, Footer
    ├── components/
    │   ├── astro/                     # Navbar (con /assets/logo.svg), Footer, CategoryCard, Breadcrumbs, HeroBanner
    │   └── react/                     # Islas interactivas:
    │       ├── CurrencySwitcher.tsx   # Selector USD / USDT / USDC / BTC / ETH
    │       ├── RegionSelector.tsx     # Selector destacado USA, Global, MX, PA, CO
    │       ├── CatalogExplorer.tsx    # Filtros (región + incluir Global, tipo, stock, búsqueda) + paginación
    │       └── ProductDetailPanel.tsx # Selector de denominación/región hermana, precio live, requisitos y CTA "Próximamente"
    └── pages/
        ├── index.astro                # Redirección 302 a /es/ o /en/
        ├── robots.txt.ts              # Dinámico según PUBLIC_SITE_URL (bloquea /api/)
        ├── sitemap.xml.ts             # Dinámico con rutas bilingües y categorías activas (usando ProviderUid)
        ├── 404.astro                  # Página 404 bilingüe con branding Verkku Games
        ├── api/                       # BFF en Cloudflare (oculta URL y API key de la Raspberry Pi):
        │   ├── items.ts               # Proxy validado hacia /api/v1/catalog/items
        │   ├── price.ts               # Proxy validado hacia /api/v1/catalog/items/{providerUid}/{itemId}/price
        │   └── rates.ts               # Proxy validado hacia /api/v1/rates
        └── [lang]/
            ├── index.astro            # Home: Hero gamer, selector rápido de región, big categories, destacadas
            ├── catalog/
            │   └── index.astro        # Explorador completo SSR + isla React
            ├── category/
            │   └── [providerUid]/[id].astro  # Productos de la categoría, selector de región y requisitos
            ├── product/
            │   └── [providerUid]/[id].astro  # Ficha individual con precio live, cripto, requisitos, JSON-LD Product y "Próximamente"
            └── studio/
                └── index.astro        # Sección ligera Verkku Games Studio (próximamente)
```

### 6.3 Seguridad y SEO en Frontend
- `API_BASE_URL` y `API_CLIENT_KEY` son secretos/variables solo de servidor en Cloudflare Workers (nunca `PUBLIC_*`).
- `src/middleware.ts` aplica `Content-Security-Policy` estricta, `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin` y `Permissions-Policy`.
- Imágenes externas del proveedor: validación estricta `https://`, `loading="lazy"`, `referrerpolicy="no-referrer"`, y fallback a `/assets/placeholder-product.svg`.
- SEO completo: `robots.txt`, `sitemap.xml` con `xhtml:link` bilingüe, `<html lang>`, `canonical`, `hreflang` (`es`, `en`, `x-default`), OpenGraph, Twitter Cards y JSON-LD (`Organization`, `WebSite`, `BreadcrumbList`, `Product`/`Offer`).

---

## 7. Documentación, Rules y Agents del Proyecto

Dentro de `F:\Verkku\local\verkku-games\`:
- `README.md` (raíz), `backend/README.md`, `frontend/README.md`.
- `AGENTS.md` (guía raíz para agentes de IA con arquitectura, prohibición de mocks en runtime, ocultamiento de proveedor por UID, políticas de seguridad y comandos de verificación).
- `.opencode/rules/`:
  - `architecture.md` (Clean Architecture .NET 10, límites de capas, sin MediatR/AutoMapper).
  - `security.md` (Bun supply-chain hardening, NuGet auditing, secretos, BFF/Cloudflare boundary, rate limiting).
  - `frontend-seo-i18n.md` (Astro SSR + React islands, i18n ES/EN, SEO, accesibilidad y marca Verkku Games).
- `.opencode/agents/`:
  - `backend-architect.md`
  - `frontend-astro.md`
  - `security-auditor.md`
- `docker-compose.yml` (Valkey/Redis + plantilla opcional para el contenedor ARM64 de la API) y `.env.example` en raíz, `backend/` y `frontend/`.

---

## 8. Decisiones Delegadas al Constructor / Plan de Implementación

- Composición detallada de componentes Tailwind/CSS y micro-interacciones dentro de la paleta oficial Verkku.
- Detalles de serialización JSON (`System.Text.Json` camelCase) y estructura interna de DTOs/records.
- Cobertura exacta de suites de prueba siempre que incluyan: `Money`, `PricingService` (incluyendo piso `MinimumMarginUsd` y override `0%`), `RegionResolver` + `ExtractBaseTitle`, `Shop2TopUpCatalogProvider` (`prices_locked`, `NO_REQUIREMENTS_FOUND`, `429`, `401/403`), `BinanceExchangeRateProvider`, validadores FluentValidation, autenticación `X-Api-Client-Key` y particionado de rate limiting.
