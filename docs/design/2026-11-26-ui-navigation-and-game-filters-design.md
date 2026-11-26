# UI: catálogos separados + filtros de juegos — Diseño (corto)

- **Fecha:** 2026-11-26
- **Estado:** Aprobado por delegación explícita ("toma la batuta").

## Intención
Separar la navegación de **recargas/gift cards** (SHOP2TOPUP) de **juegos completos** (FazerCards), y añadir filtros propios de videojuegos: consola/plataforma, categoría (género), año de lanzamiento y multijugador.

## Decisiones (batuta)
1. **Dos rutas**
   - `/[lang]/catalog` → recargas + gift cards (SHOP2TOPUP, `ProviderUid` por defecto).
   - `/[lang]/games` → juegos completos (FazerCards, `ProviderUid` fijo `9075f324-4177-45e9-a79c-f23722258b8f`).
   - Navbar: Inicio · Catálogo · Juegos · Studio. Home: 3 tarjetas apuntan a esas rutas.
2. **Metadatos de juegos**: FazerCards entrega `platform` (Steam/EA/Xbox), `region`, `appid`, `imageurl`; NO año/género/multijugador. Se enriquece con `GET https://store.steampowered.com/api/appdetails?appids={appid}&cc=us&l=english` (público, verificado): `release_date.date`, `genres[].description`, `categories[].description` (Multi-player / Co-op / PvP), `platforms`. Cache 12 h por appid en Redis vía `ICacheService`. Si falla o no hay appid → metadatos vacíos (sin filtros falsos).
3. **Filtros server-side** en `/api/v1/catalog/items`: `platform`, `genre`, `yearFrom`, `yearTo`, `multiplayerOnly`. Se añaden a `GetCatalogItemsQuery` + validador + handler + endpoint + BFF. La paginación se calcula tras filtrar.
4. **Rendimiento en frío**: el barrido de juegos y el enriquecimiento Steam se paralelizan con `SemaphoreSlim(8)` (antes era secuencial → >30 s). Cache Redis 10 min para catálogo y 12 h para metadatos.
5. **Sin cambios de contrato rompientes**: nuevos campos opcionales en `CatalogItem`/DTO (`GameMetadata?`), filtros opcionales en query. SHOP2TOPUP sigue igual.

## No-objetivos
- Checkout, órdenes ni webhooks (v1 sigue solo lectura).
- CMS de metadata; los datos vienen del proveedor + Steam.
- "Consolas" como enumerated estricto: se muestra el `platform` real del proveedor (Steam=PC, Xbox, EA) y los OS de Steam como detalle.

## Arquitectura
- Domain: `GameMetadata(ReleaseYear?, IReadOnlyList<string> Genres, bool IsMultiplayer, IReadOnlyList<string> SupportedPlatforms, string? Developer)`; `CatalogItem` + `GameMetadata? Metadata`.
- Application: `IGameMetadataProvider` (puerto) + `GetCatalogItemsQuery` extendido.
- Infrastructure: `SteamGameMetadataProvider` (HttpClient + cache), FazerCards enriquece items en paralelo.
- API: parámetros nuevos parseados (año 1970..2100, género/consola max 64 chars alfanuméricos).
- Frontend: `/games` usa `CatalogExplorer` con `mode="games"` (muestra filtros nuevos); `/catalog` sigue con filtros actuales; i18n ES/EN; BFF reenvía parámetros.

## Riesgo
Steam `appdetails` puede limitar o no tener `appid` (~10% de juegos). En ese caso el juego se muestra sin año/género/multijugador y no coincide con esos filtros — nunca se inventan datos.
