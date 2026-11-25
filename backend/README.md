# Verkku Games — Backend (.NET 10)

API de catálogo y precios de Verkku Games. Clean Architecture, Minimal APIs, **.NET 10**, caché distribuida con **Valkey/Redis** vía `HybridCache`, lista para correr en **Raspberry Pi ARM64** detrás de Cloudflare Tunnel.

- **Solución:** `backend/VerkkuGames.slnx`
- **SDK:** `10.0.402` (fijado en `global.json`, `rollForward: latestFeature`)
- **Target:** `net10.0`
- **Estilo:** `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `Deterministic=true`
- **Puerto local:** `http://localhost:5180` (`ASPNETCORE_URLS` en `.env.example`)
- **En producción:** `http://+:8080` dentro del contenedor (loopback `127.0.0.1:8080` en el host)

---

## 1. Capas y reglas de dependencia

```text
VerkkuGames.Api  ---------->  Application  --->  Domain
       |                          ^
       +---------->  Infrastructure+
```

| Proyecto | Puede referenciar | Responsabilidad | Prohibido |
| --- | --- | --- | --- |
| `VerkkuGames.Domain` | **nada** | Value objects (`Money`, `CurrencyCode`, `RegionCode`, `ProductType`), modelos de catálogo normalizados, `Result<T>`/`Error` | Paquetes NuGet, `HttpClient`, EF, ASP.NET |
| `VerkkuGames.Application` | `Domain` | Puertos (`ICatalogProvider`, `ICacheService`, `IPricingService`, `IRegionResolver`, `IExchangeRateProvider`, `IQueryHandler<,>`), queries, validadores FluentValidation, pricing, regiones | `HttpClient` concreto, Redis, Minimal APIs |
| `VerkkuGames.Infrastructure` | `Application`, `Domain` | Adaptadores: SHOP2TOPUP, Binance, `HybridCache`/Redis, resiliencia HTTP, DI de infraestructura | Tipos de `Api` |
| `VerkkuGames.Api` | `Application`, `Infrastructure` | Endpoints `/api/v1/*`, autenticación BFF, rate limiting, ProblemDetails, salud, OpenAPI (solo dev) | Lógica de negocio o llamadas HTTP directas |
| `tests/UnitTests` | Domain, Application, Infrastructure | xUnit + NSubstitute/fakes: Money, pricing, regiones, parsers, validadores | — |
| `tests/IntegrationTests` | Api | `WebApplicationFactory` + `HttpMessageHandler` fake + caché in-memory/Redis | Llamar a SHOP2TOPUP/Binance reales |

**Reglas duras**

- `Domain` no referencia ningún paquete externo. Si necesitas un atributo de serialización ahí, estás en la capa equivocada.
- Los controladores/endpoints **no** conocen `HttpClient`, Redis ni SHOP2TOPUP: solo `IQueryHandler`.
- `Infrastructure` nunca se referencia desde `Application`.
- **Cero MediatR, cero AutoMapper.** CQRS liviano propio + mapeo explícito.
- El dinero y las tasas son `decimal` con `CultureInfo.InvariantCulture`. Nunca `float`/`double`.

---

## 2. Cómo agregar un nuevo `ICatalogProvider`

Cada proveedor mayorista se identifica internamente por `InternalKey` y públicamente por un **`Guid` opaco**.

```csharp
public interface ICatalogProvider
{
    string InternalKey { get; }        // "shop2topup" — SOLO interno (logs/config)
    Guid ProviderUid { get; }          // UID público estable y opaco

    Task<IReadOnlyList<CatalogBigCategory>> GetBigCategoriesAsync(CancellationToken ct);
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(string? bigCategoryId, CancellationToken ct);
    Task<IReadOnlyList<CatalogItem>> GetItemsAsync(string? categoryId, CancellationToken ct);
    Task<Result<ItemPriceQuote>> GetItemPriceAsync(string itemId, CancellationToken ct);
    Task<Result<IReadOnlyList<CategoryRequirement>>> GetCategoryRequirementsAsync(string categoryId, CancellationToken ct);
    Task<bool> CheckHealthAsync(CancellationToken ct);
}
```

Pasos:

1. **Crea la opción tipada** en `Infrastructure` con `ValidateDataAnnotations()` (por ejemplo `MyProviderOptions` con `BaseUrl`, `KeyId`, `KeySecret`, `ProviderUid`).
2. **Implementa el adaptador** en `Infrastructure/<Proveedor>/`. Todo `HttpClient` va con `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`:
   - timeout **8 s** por intento;
   - retry exponencial con jitter **solo** para `5xx`, `408` y errores de red;
   - **nunca** reintentar `400/401/403/404`;
   - circuit breaker activo.
3. **Normaliza** la respuesta a los modelos del `Domain`. Aplica:
   - `decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture)` para precios;
   - `Money` con redondeo por moneda (`USD/USDT/USDC: 2`, `ETH: 6`, `BTC: 8`, `MidpointRounding.AwayFromZero`);
   - validación de `ImageUrl`: solo `https://` absoluto y bien formado; cualquier otro valor (`http:`, `javascript:`, `data:`, basura) se normaliza a `null`;
   - herencia de imagen desde la categoría cuando el ítem no trae una.
4. **Registra el `ProviderUid`** en configuración (`ProviderUid` con default determinista) y **registra el proveedor** en el contenedor con su `ICatalogProvider`.
5. **Regístralo en `ICatalogProviderRegistry`**. El registry resuelve en ambos sentidos y expone solo `ProviderUid`s:

```csharp
public interface ICatalogProviderRegistry
{
    ICatalogProvider? GetByUid(Guid providerUid);
    ICatalogProvider GetDefault();
    IReadOnlyList<ICatalogProvider> GetAll();
}
```

6. **Prohíbe el `InternalKey` en la superficie pública.** Rutas, DTOs, logs de request y SEO usan `ProviderUid`.
7. **Tests obligatorios del adaptador:** éxito, `prices_locked`, error de auth (`401/403`), error de cuenta congelada/IP no permitida, `429` con `Retry-After`, `404` de requisitos, JSON malformado y parseo bajo cultura `es-VE`/`es-ES`.

> **Nunca** agregues un `if (provider == "shop2topup")` en `Application` o `Api`. Las diferencias del proveedor se resuelven dentro de su adaptador.

---

## 3. Precios (`PricingOptions` + `IPricingService`)

Configuración:

| Opción | Default | Rango |
| --- | --- | --- |
| `DefaultMarkupPercent` | `10.0m` | `0..500` |
| `MinimumMarginUsd` | `0.25m` | `0..1000` |
| `CategoryMarkupOverrides` | `{}` | `categoryId -> percent` |

El **margen mínimo es un piso (floor)**, no un cargo aditivo:

```text
effectivePercent = overrides.TryGetValue(categoryId, out var ov) ? ov : DefaultMarkupPercent

if effectivePercent == 0m:
    retailUsd = cost
else:
    retailUsd = Math.Max(cost * (1 + effectivePercent / 100), cost + MinimumMarginUsd)

retailUsd = Math.Round(retailUsd, 2, MidpointRounding.AwayFromZero)
```

| Costo USD | Markup | Mínimo | Resultado | Por qué |
| --- | --- | --- | --- | --- |
| `1.00` | 10 % | 0.25 | **1.10** | El porcentaje supera al piso |
| `0.50` | 10 % | 0.25 | **0.75** | `max(0.55, 0.75)` → gana el piso |
| `0.10` | 10 % | 0.25 | **0.35** | El piso protege tickets pequeños |
| `1.00` | **0 %** | 0.25 | **1.00** | Override explícito a 0 % desactiva el piso (promoción) |

**Invariantes**

- `0 %` explícito es la **única** forma de vender al costo.
- El costo reseller (`unit_price`, `original_price`) **nunca** se serializa en respuestas públicas; solo el precio retail calculado.
- Si el proveedor devuelve `prices_locked`/`RELOAD_REQUIRED`, el ítem se lista con `RetailPriceUsd = null` y `PricesLocked = true` (el listado no se rompe); la consulta de precio en vivo responde un error `PricesLocked` controlado.
- El redondeo final siempre es 2 decimales hacia afuera.

---

## 4. Regiones (`IRegionResolver`)

Opera **solo** sobre `categoryName` y `itemName`. No llama a `/requirements` durante listados (evita fan-out N+1 y garantiza consistencia listado ↔ detalle).

Coincidencia case-insensitive con **límites de palabra**:

| Región | Regex |
| --- | --- |
| `USA` | `\b(usa|us|united states|eeuu)\b` |
| `MX` | `\b(mexico|méxico|mx)\b` |
| `PA` | `\b(panama|panamá|pa)\b` |
| `CO` | `\b(colombia|co)\b` |
| `GLOBAL` | `\b(global|worldwide|ww|international|all regions)\b` |
| — | cualquier otro caso → `OTHER` |

Filtrado en API:

- Por defecto coincidencia exacta de región (`includeGlobal=false`).
- Con `includeGlobal=true` y una región país (`USA`, `MX`, `PA`, `CO`), se incluyen también productos `GLOBAL`, marcados con badge `Global` en el frontend.
- `OTHER` existe para **no ocultar** productos válidos del proveedor.

`ExtractBaseTitle(categoryName)` limpia tokens/sufijos de región (`USA`, `Global`, `Mexico`, `MX`, `Panama`, `Colombia`, paréntesis vacíos) para enlazar categorías hermanas multi-región en la ficha de producto.

> Cuidado con falsos positivos por límite de palabra: `"US"` dentro de `"Plus"` no debe coincidir; `"CO"` dentro de `"Console"` tampoco.

---

## 5. Caché (`HybridCache`)

`.NET 10 HybridCache`: **L1 en memoria + L2 Redis** (`Microsoft.Extensions.Caching.StackExchangeRedis`). Evita *cache stampede* y mantiene baja la huella de RAM en la RPi.

| Dato | TTL | Clave (prefijo `verkkugames:v1:`) |
| --- | --- | --- |
| Big categories | 30 min | `catalog:{uid}:big-categories` |
| Categories | 15 min | `catalog:{uid}:categories:{bigCategoryId}` |
| Items (subcategorías) | 10 min | `catalog:{uid}:items:{categoryId}` |
| Requirements | 30 min | `catalog:{uid}:requirements:{categoryId}` |
| Precio en vivo | 60 s | `price:{uid}:{itemId}` |
| Tasas cripto | 60 s | `rates:crypto` |

Reglas:

- El prefijo sale de `Cache__KeyPrefix` (`verkkugames:v1:`).
- Las claves **nunca** contienen el `InternalKey` del proveedor; solo el `ProviderUid`.
- No caches errores de autenticación ni `ProblemDetails`; sí puedes cachear listas vacías legítimas (p. ej. `NO_REQUIREMENTS_FOUND` → `[]`).
- En tests, `ICacheService` se sustituye por una implementación in-memory; en producción el Redis es obligatorio.

---

## 6. Contrato con SHOP2TOPUP (interno) y Binance

Base: `https://shop2topup.com/api/endpoints/v1` · Auth: `Authorization: Bearer {KeyId}.{KeySecret}`.

| Endpoint | Uso | Notas |
| --- | --- | --- |
| `GET /catalog/big-categories?for_ui=true` | Árbol nivel 1 | — |
| `GET /catalog/categories?for_ui=true[&bigCategoryId=]` | Nivel 2 | `bigCategoryId` opcional |
| `GET /catalog/subcategories[?categoryId=]` | Ítems + stock + precio | `categoryId` opcional; puede traer `prices_locked` |
| `GET /catalog/subcategory/{itemId}/price` | Precio en vivo | Devuelve `unit_price` (**privado**) |
| `GET /catalog/category/{categoryId}/requirements` | Requisitos del juego | `NO_REQUIREMENTS_FOUND` → `[]` |
| `GET /account` | Readiness interno | Cacheado 5 min, **nunca** expuesto |

Mapeo de errores del proveedor:

| Código / señal | Resultado interno | Respuesta pública |
| --- | --- | --- |
| `RELOAD_REQUIRED` (403), `prices_locked: true` | `PricesLocked` | `422`/`503` controlado; listado con precio `null` |
| `NO_REQUIREMENTS_FOUND` (404) | lista vacía | `200 []` |
| `ITEM_NOT_FOUND` (404) | `NotFound` | `404` ProblemDetails |
| `RATE_LIMIT_EXCEEDED` (429) | `RateLimited` + `Retry-After` | `429` |
| `PRICING_UPDATING` (409) | `Conflict` + `Retry-After` | `409` |
| `MISSING_API_KEY` / `INVALID_API_KEY` / `IP_NOT_ALLOWED` / `ACCOUNT_FROZEN` | log estructurado `Error` | `503 ProviderUnavailable` **genérico** |

**Binance** (`https://data-api.binance.vision`, sin API key):

```text
GET /api/v3/ticker/price?symbols=["BTCUSDT","ETHUSDT","USDCUSDT"]

USD  = 1
USDT = 1
USDC = 1 / usdcUsdt
BTC  = 1 / btcUsdt
ETH  = 1 / ethUsdt
```

Todo `decimal` con `InvariantCulture`. No se exponen detalles internos innecesarios en `/api/v1/rates`.

---

## 7. Endpoints (`/api/v1`)

| Método | Ruta | Descripción |
| --- | --- | --- |
| GET | `/api/v1/catalog/big-categories?providerUid=` | Categorías raíz |
| GET | `/api/v1/catalog/categories?providerUid=&bigCategoryId=&region=&includeGlobal=false&search=` | Categorías |
| GET | `/api/v1/catalog/items?providerUid=&categoryId=&region=&includeGlobal=false&productType=&inStockOnly=false&search=&page=1&pageSize=24` | Ítems paginados |
| GET | `/api/v1/catalog/items/{providerUid:guid}/{itemId}` | Detalle + requisitos + hermanos + regiones hermanas |
| GET | `/api/v1/catalog/items/{providerUid:guid}/{itemId}/price` | Precio en vivo |
| GET | `/api/v1/catalog/categories/{providerUid:guid}/{categoryId}/requirements` | Requisitos |
| GET | `/api/v1/rates` | Tasas cripto por USD |
| GET | `/health/live` | Liveness |
| GET | `/health/ready` | Readiness (Redis + config, sin datos sensibles) |

**Seguridad de la superficie**

- Todos los `/api/v1/*` exigen `X-Api-Client-Key`; comparación con `CryptographicOperations.FixedTimeEquals`.
- Tras autenticar, se confía en `X-Forwarded-Client-Ip` (enviado por el BFF desde `CF-Connecting-IP`) para **rate limiting por IP final**: ~120 req/min general, ~30 req/min en `/price`, más un tope global.
- `X-Forwarded-Client-Ip` **nunca** se usa antes de autenticar: un cliente no autenticado con cabecera falsa recibe `401`.
- ProblemDetails RFC 9457, `AddServerHeader = false`, cabeceras `nosniff` / `DENY` / `no-referrer`, CORS desde configuración (vacío en producción).
- OpenAPI solo en `Development`. `ValidateDataAnnotations().ValidateOnStart()` en todas las opciones.

---

## 8. Configuración local

`dotnet user-secrets` (recomendado en dev). Nota: en User Secrets y `appsettings` se usa `:`; en variables de entorno, `__`.

```bash
cd backend/src/VerkkuGames.Api
dotnet user-secrets set "Shop2TopUp:KeyId" "tu_key_id"
dotnet user-secrets set "Shop2TopUp:KeySecret" "tu_key_secret"
dotnet user-secrets set "Shop2TopUp:ProviderUid" "d4f8a2c1-7b3e-4f90-9a12-6e8b5c3d1f42"
dotnet user-secrets set "ApiSecurity:ClientApiKey" "change_me_local_dev_bff_key_32bytes"
dotnet user-secrets set "Cache:RedisConnectionString" "localhost:6379,password=change_me_local_dev_only,abortConnect=false"
dotnet user-secrets set "Pricing:DefaultMarkupPercent" "10.0"
dotnet user-secrets set "Pricing:MinimumMarginUsd" "0.25"
dotnet user-secrets list
```

Valkey local:

```bash
docker compose up -d valkey
docker compose ps
```

> `UserSecretsId` del proyecto API: `verkku-games-api-v1`.

---

## 9. Tests y auditorías

```bash
# desde backend/
dotnet build VerkkuGames.slnx
dotnet test  VerkkuGames.slnx
dotnet list VerkkuGames.slnx package --vulnerable --include-transitive
```

Cobertura mínima esperada:

- `Money` (redondeo por moneda, negativos rechazados, `InvariantCulture`).
- `PricingService`: piso `MinimumMarginUsd`, override `0 %`, overrides por categoría, redondeo.
- `RegionResolver` + `ExtractBaseTitle`: límites de palabra, `OTHER`, regiones hermanas.
- `Shop2TopUpCatalogProvider`: `prices_locked`, `NO_REQUIREMENTS_FOUND`, `429`, `401/403`, `RELOAD_REQUIRED`, cultura no inglesa.
- `BinanceExchangeRateProvider`: tasas por USD y precisión BTC/ETH.
- Validadores FluentValidation.
- Integración: `X-Api-Client-Key` (401 sin clave / clave inválida), particionado de rate limiting por `X-Forwarded-Client-Ip`, ausencia de `"shop2topup"` y de `unit_price` en JSON público, `health/live` y `health/ready`.

---

## 10. Despliegue en Raspberry Pi (ARM64)

### Opción A — Docker Compose (`--profile api`)

```bash
# en la RPi, con el repo clonado
cp .env.example .env    # completar API_CLIENT_KEY, SHOP2TOPUP_*, VALKEY_PASSWORD
docker compose --profile api up -d --build
docker compose logs -f api
```

- `backend/Dockerfile` es multi-stage, publica `linux-arm64` (o `linux-x64`) y corre como **usuario no-root**. Se incorpora junto con la capa `Api`; si aún no existe en tu checkout, usa la Opción B.
- El compose publica `127.0.0.1:8080:8080`: solo loopback, para que `cloudflared` lo alcance sin exponer LAN/WAN.
- Valkey queda en la red interna `verkku-internal` sin puerto público en producción.

### Opción B — binario nativo + `systemd`

```bash
dotnet publish src/VerkkuGames.Api -c Release -r linux-arm64 --self-contained false -o /opt/verkkugames/api
```

`/etc/systemd/system/verkkugames-api.service`:

```ini
[Unit]
Description=Verkku Games API
After=network.target

[Service]
WorkingDirectory=/opt/verkkugames/api
ExecStart=/usr/bin/dotnet /opt/verkkugames/api/VerkkuGames.Api.dll
Restart=always
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
EnvironmentFile=/etc/verkkugames/api.env    # secretos, permisos 600
User=verkkugames

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now verkkugames-api
```

### Cloudflare Tunnel

```bash
cloudflared tunnel create verkku-games-api
cloudflared tunnel route dns verkku-games-api api.games.verkku.com
cloudflared tunnel run verkku-games-api    # origen: http://127.0.0.1:8080
```

Checklist de producción:

- [ ] IP pública de la RPi autorizada en el panel SHOP2TOPUP (allowlist).
- [ ] Saldo reseller suficiente (evita `RELOAD_REQUIRED` / `prices_locked`).
- [ ] `Cache__RedisConnectionString` con password y `abortConnect=false`.
- [ ] `ApiSecurity__ClientApiKey` idéntico al `API_CLIENT_KEY` del Worker.
- [ ] `GET /health/ready` en `200` a través del túnel.
- [ ] Logs sin secretos, sin `unit_price` y sin `InternalKey` en nivel `Information`.

---

## 11. Errores comunes

| Síntoma | Causa probable | Solución |
| --- | --- | --- |
| `401` en todas las rutas `/api/v1/*` | `X-Api-Client-Key` ausente o distinto | Igualar `API_CLIENT_KEY` (Worker) y `ApiSecurity__ClientApiKey` (API) |
| `503` genérico con `IP_NOT_ALLOWED` en logs | IP de salida no autorizada | Agregar IP al allowlist del panel reseller |
| Precios `null` en todo el catálogo | `prices_locked` / `RELOAD_REQUIRED` | Recargar saldo en SHOP2TOPUP |
| `500` al parsear precios | Cultura del thread (`es-VE`) | Usar `CultureInfo.InvariantCulture` siempre |
| Rate limit global disparado por un usuario | Se confió en `X-Forwarded-Client-Ip` antes de autenticar o partición incorrecta | Autenticar primero; particionar por IP final |
| `docker compose` no levanta la API | Falta `--profile api` | `docker compose --profile api up -d --build` |

---

Ver también: [`../README.md`](../README.md) · [`../frontend/README.md`](../frontend/README.md) · [`../AGENTS.md`](../AGENTS.md) · [`.opencode/rules/architecture.md`](../.opencode/rules/architecture.md).
