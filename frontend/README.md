# Verkku Games — Frontend (Astro 7 + React 19 en Cloudflare Workers)

Storefront bilingüe **ES/EN**, SSR en el edge, SEO-first. Astro renderiza HTML en el servidor y React 19 se usa **solo** en islas interactivas. El frontend también es el **BFF**: el navegador nunca ve la URL ni la API key de la Raspberry Pi.

- **Framework:** Astro `^7.4.12` (`output: "server"`)
- **Adapter:** `@astrojs/cloudflare` `^14.4.8` → **Workers with Static Assets**
- **UI:** React `^19.3.1` + `@astrojs/react` `^7.1.9`
- **Estilos:** Tailwind CSS `^4.3.5` + `@tailwindcss/vite`
- **Tipos:** TypeScript `^5.9.3` (strict, `noUncheckedIndexedAccess`)
- **Runtime/gestor:** **Bun `1.4.5`**
- **CLI edge:** Wrangler `^4.164.0`
- **Dev:** `http://localhost:4321` → redirige a `/es/`
- **Producción:** `https://games.verkku.com`

> **Por qué Astro 7:** el scaffold inicial en Astro 5 acumulaba 49 CVEs reportados por `bun audit`. La actualización a Astro 7 + `@astrojs/cloudflare@14` + `@astrojs/react@7` deja **0 vulnerabilidades**.

---

## 1. Estructura

```text
frontend/
├── bunfig.toml                 # hardening de instalación
├── package.json                # scripts: dev, build, preview, check, test, audit
├── astro.config.mjs            # output: server, cloudflare, i18n, tailwind
├── wrangler.jsonc              # Workers + Static Assets
├── tsconfig.json               # astro/tsconfigs/strict + alias @/*
├── public/
│   ├── assets/
│   │   ├── logo.svg            # logo oficial Verkku (ÚNICO nombre permitido)
│   │   └── placeholder-product.svg
│   ├── favicon.svg
│   └── site.webmanifest
└── src/
    ├── middleware.ts           # CSP, HSTS, nosniff, DENY, Referrer-Policy, Permissions-Policy
    ├── i18n/
    │   ├── config.ts           # locales ['es','en'], defaultLocale 'es'
    │   ├── es.ts               # copy gamer natural en español
    │   ├── en.ts               # copy gamer natural en inglés
    │   └── utils.ts            # traductor tipado + helpers hreflang
    ├── lib/
    │   ├── api-client.ts       # SOLO servidor: X-Api-Client-Key + X-Forwarded-Client-Ip
    │   ├── currency.ts         # formateo USD/USDT/USDC/BTC/ETH
    │   ├── regions.ts          # etiquetas ES/EN de USA, GLOBAL, MX, PA, CO, OTHER
    │   └── seo.ts              # meta, OpenGraph, JSON-LD
    ├── layouts/BaseLayout.astro
    ├── components/
    │   ├── astro/              # Navbar, Footer, CategoryCard, Breadcrumbs, HeroBanner
    │   └── react/              # CurrencySwitcher, RegionSelector, CatalogExplorer, ProductDetailPanel
    └── pages/
        ├── index.astro         # 302 → /es/ o /en/
        ├── robots.txt.ts       # dinámico, bloquea /api/
        ├── sitemap.xml.ts      # bilingüe con xhtml:link
        ├── 404.astro
        ├── api/                # BFF: items.ts · price.ts · rates.ts
        └── [lang]/
            ├── index.astro
            ├── catalog/index.astro
            ├── category/[providerUid]/[id].astro
            ├── product/[providerUid]/[id].astro
            └── studio/index.astro
```

---

## 2. Astro 7 SSR en Cloudflare Workers

`astro.config.mjs` (resumen real):

```js
export default defineConfig({
  site: process.env.PUBLIC_SITE_URL?.trim() || 'https://games.verkku.com',
  output: 'server',
  adapter: cloudflare({ imageService: 'passthrough' }),
  integrations: [react()],
  i18n: { locales: ['es', 'en'], defaultLocale: 'es', routing: { prefixDefaultLocale: true } },
  security: { checkOrigin: true },
  vite: { plugins: [tailwindcss()] },
});
```

`wrangler.jsonc` (resumen real):

```jsonc
{
  "name": "verkku-games",
  "main": "@astrojs/cloudflare/entrypoints/server",
  "compatibility_date": "2026-01-01",
  "compatibility_flags": ["nodejs_compat"],
  "assets": { "binding": "ASSETS" },
  "observability": { "enabled": true },
  "vars": {
    "PUBLIC_SITE_URL": "https://games.verkku.com",
    "API_BASE_URL": "https://api.games.verkku.com"
  }
}
```

Notas:

- `imageService: 'passthrough'` evita depender de Cloudflare Images; las imágenes externas se validan y renderizan directo.
- `security.checkOrigin: true` protege formularios/POSTs contra cross-origin.
- `main` apunta al entrypoint del adapter; **no** lo cambies a un archivo local.
- `nodejs_compat` es obligatorio para el runtime del adapter y utilidades de Node.
- El BFF vive en `src/pages/api/*` y corre en el Worker; ahí se inyectan los secretos de servidor.

### Variables de entorno

| Variable | ¿Pública? | Dónde se define | Uso |
| --- | --- | --- | --- |
| `PUBLIC_SITE_URL` | Sí (intencional) | `wrangler.jsonc` `vars` / `.env` local | canonical, hreflang, OG, `robots.txt`, `sitemap.xml` |
| `API_BASE_URL` | **No** | `wrangler.jsonc` `vars` / `.env` local | URL del túnel hacia la RPi (solo servidor) |
| `API_CLIENT_KEY` | **No** | `wrangler secret put` / `.env` local | clave compartida con la API .NET |

> **Nunca** prefijes con `PUBLIC_` nada que sea secreto. Todo lo `PUBLIC_*` termina en el bundle del navegador.

---

## 3. Disciplina de islas React

Astro genera el HTML; React solo hidrata lo que necesita interacción.

| Isla | Directiva | Por qué |
| --- | --- | --- |
| `CurrencySwitcher` | `client:load` | Cambio inmediato de equivalente cripto (persistido en `localStorage`) |
| `RegionSelector` | `client:load` | Filtro principal de la tienda |
| `CatalogExplorer` | `client:visible` | Filtros + paginación; no bloquea el primer render |
| `ProductDetailPanel` | `client:visible` | Precio en vivo, requisitos y CTA "Próximamente" |

**Reglas**

- Todo lo que sea contenido/enlace/SEO va en `.astro`, no en React.
- Nunca uses `client:only` para contenido indexable.
- Una isla = una responsabilidad; no metas el catálogo completo en un solo componente React.
- El estado de moneda/región se comparte vía `localStorage` + eventos, sin store global pesado.
- `useEffect` para `localStorage` (nunca en el render inicial, para evitar hydration mismatch).
- Nada de `fetch` a la Raspberry Pi desde el navegador: las islas llaman a `/api/*` (BFF).

La ficha v1 es **solo consulta**: muestra precio, equivalentes, stock, región y requisitos, con CTA **"Próximamente" / "Coming soon"**. Sin botón falso de compra ni enlaces a WhatsApp/Telegram.

---

## 4. Tailwind v4 y marca Verkku

Tailwind v4 se integra por Vite (sin `tailwind.config.js`). El CSS global usa `@import "tailwindcss";` y tokens con `@theme`.

### Paleta oficial

| Token | Hex | Uso |
| --- | --- | --- |
| `bg-base` | `#05342E` / `#06352F` | Fondo oscuro principal (default gamer) |
| `surface` | `#0A453D` / `#0E4F46` | Cards y superficies elevadas |
| `brand` | `#1A8154` / `#209959` | CTAs primarios (hover `#1F9087`) |
| `accent` | `#25C877` | Highlights, badges, foco |
| `accent-soft` | `#81CB79` / `#8BCCAB` | Detalles y estados |
| `teal` | `#2E6D6D` | Secundario |
| `light` | `#FBF7F5` / `#EFF0E9` | Superficies claras / contraste invertido |

- **Logo:** `frontend/public/assets/logo.svg` (nombre estricto). En la navbar va acompañado de la insignia `GAMES`.
- **Favicon / placeholder:** `public/favicon.svg` y `public/assets/placeholder-product.svg`, derivados de la paleta.
- **Tipografía:** `Bricolage Grotesque` (titulares) + `Poppins` (cuerpo), `display=swap` y fallback de sistema.
- **Contraste:** todo texto sobre fondo debe cumplir **WCAG AA** (4.5:1 para cuerpo). No uses `#25C877` como color de texto pequeño sobre `#05342E` sin verificar.

---

## 5. Hardening de cadena de suministro con Bun

`bunfig.toml` (contenido real):

```toml
[install]
# Enfriamiento de 7 días para paquetes recién publicados (mitiga ataques de supply chain)
minimumReleaseAge = 604800
# Resolución de versiones exactas
exact = true
```

Buenas prácticas obligatorias:

```bash
bun install --frozen-lockfile   # CI y despliegue: el lockfile manda
bun audit                       # debe reportar 0 vulnerabilidades
bun pm untrusted                # listar dependencias con scripts de postinstall no confiables
```

- `minimumReleaseAge = 604800` bloquea versiones npm publicadas hace menos de **7 días**.
- `exact = true` fija versiones exactas al instalar (sin rangos implícitos).
- `trustedDependencies` debe quedar vacío o mínimo y auditado; ningún paquete corre postinstall por defecto.
- `bun.lock` se commitea. No se commitea `node_modules/`.
- Antes de agregar una dependencia: licencia compatible, mantenimiento activo, sin CVEs, y justificar por qué Astro/React no lo cubren.
- `bun run preview` usa `wrangler dev`, no el preview de Astro: valida el runtime real de Workers.

---

## 6. i18n ES/EN

- Locales: `es` (default) y `en`; `prefixDefaultLocale: true` → **todas** las rutas llevan prefijo (`/es/...`, `/en/...`).
- `/` responde `302` según cookie de preferencia o `Accept-Language`, con fallback a `/es/`.
- Diccionarios **tipados** en `src/i18n/es.ts` y `src/i18n/en.ts`; el tipo del diccionario ES es la fuente de verdad y EN debe satisfacerlo (paridad de claves garantizada por TypeScript).
- `src/i18n/utils.ts` expone el traductor tipado y los helpers de `hreflang`.
- **Cero copy hardcodeado** en páginas o componentes: todo texto visible sale del diccionario.
- Voz: natural, cercana y gamer, sin perder claridad y honestidad técnica.
- Nombres propios y siglas no se traducen (`Verkku Games`, `Gift Card`, `USA`, `BTC`).
- Los mensajes de error de UI son claros y accionables; nunca exponen errores crudos del proveedor.

Checklist al agregar una cadena:

- [ ] Clave agregada en `es.ts` **y** en `en.ts`.
- [ ] Sin texto concatenado a mano para formar frases (usa placeholders/interpolación).
- [ ] Plurales y fechas/moneda formateados con `Intl`.
- [ ] `bun run check` pasa (paridad de tipos).

---

## 7. SEO checklist

| Elemento | Implementación | Verificación |
| --- | --- | --- |
| `robots.txt` | `src/pages/robots.txt.ts` según `PUBLIC_SITE_URL`; bloquea `/api/` | `curl localhost:4321/robots.txt` |
| `sitemap.xml` | `src/pages/sitemap.xml.ts` con rutas ES/EN y `xhtml:link` hreflang | revisar que no aparezca `"shop2topup"` |
| `canonical` | En `BaseLayout.astro`, absoluto con `PUBLIC_SITE_URL` | Inspeccionar `<head>` |
| `hreflang` | `es`, `en`, `x-default` recíprocos | Cada página enlaza a su par |
| `<html lang>` | `es`/`en` según ruta | Inspeccionar HTML |
| OpenGraph / Twitter | `og:title`, `og:description`, `og:image`, `og:url`, `twitter:card` | Vista previa social |
| JSON-LD | `Organization`, `WebSite`, `BreadcrumbList`, `Product`/`Offer` | Rich Results Test |
| Imágenes | Solo `https://`, `loading="lazy"`, `referrerpolicy="no-referrer"`, fallback `placeholder-product.svg` | Buscar `http://` en HTML |
| Títulos | Únicos, ≤ 60 caracteres, con marca | Auditoría manual |
| 404 | Página bilingüe con branding y enlaces útiles | `/es/ruta-inexistente` |

**Reglas duras de SEO**

- Ninguna URL, meta, JSON-LD o `sitemap.xml` contiene el nombre del proveedor mayorista: solo `ProviderUid` (Guid).
- Nada de precios inventados en JSON-LD: si `RetailPriceUsd` es `null` (precios bloqueados), no se emite `Offer`.
- `sitemap.xml` solo incluye rutas canónicas con contenido real.
- No se indexan páginas de error ni `/api/*`.

---

## 8. Seguridad del frontend

`src/middleware.ts` aplica en todas las respuestas:

- `Content-Security-Policy` estricta (sin `unsafe-eval`; `img-src` limitado a `self`, `https:` y el proveedor permitido).
- `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`.

Además:

- `src/lib/api-client.ts` corre **solo en servidor** y añade `X-Api-Client-Key`; reenvía la IP real del visitante desde `CF-Connecting-IP` como `X-Forwarded-Client-Ip`.
- El BFF valida `providerUid` como `Guid`, `pageSize` con tope, y no reenvía parámetros arbitrarios.
- Nunca se renderiza HTML de terceros con `set:html` sin sanitizar.
- Errores del backend se mapean a estados de UI claros; nunca se muestran mensajes crudos.

---

## 9. Comandos

```bash
bun install
bun run dev            # astro dev → http://localhost:4321
bun test               # bun test (unidades de lib/, i18n, currency, seo)
bun run check          # astro check (TypeScript + .astro)
bun run build          # astro build → dist/ para Workers
bun run preview        # wrangler dev contra el build
bun audit              # 0 vulnerabilidades esperado
```

Verificación completa antes de terminar:

```bash
bun install --frozen-lockfile
bun test && bun run check && bun run build && bun audit
```

---

## 10. Despliegue en Cloudflare

```bash
cd frontend
bun install --frozen-lockfile
bun run build
bunx wrangler deploy
bunx wrangler secret put API_CLIENT_KEY
```

- `API_CLIENT_KEY` debe ser **idéntico** a `ApiSecurity__ClientApiKey` del backend.
- `API_BASE_URL` (var) debe apuntar al hostname del Cloudflare Tunnel de la RPi.
- `PUBLIC_SITE_URL` debe ser el dominio final (afecta canonical, hreflang, `robots.txt` y `sitemap.xml`).
- Tras el deploy: probar `/es/`, `/en/`, `/es/catalog`, una ficha de producto, `/robots.txt`, `/sitemap.xml` y el 404.

Smoke test de seguridad:

```bash
curl -sI https://games.verkku.com/es/ | grep -iE 'content-security-policy|strict-transport-security|x-frame-options|x-content-type-options'
curl -s https://games.verkku.com/sitemap.xml | grep -ci shop2topup   # debe ser 0
```

---

## 11. Errores comunes

| Síntoma | Causa | Solución |
| --- | --- | --- |
| `401` desde `/api/*` | `API_CLIENT_KEY` ausente o distinto | `wrangler secret put API_CLIENT_KEY` y reiniciar el Worker |
| `500` en SSR | `API_BASE_URL` mal configurada o túnel caído | Verificar var y `cloudflared` |
| Hydration mismatch | Leer `localStorage` durante el render | Mover a `useEffect` |
| `bun install` falla por versión reciente | `minimumReleaseAge = 604800` | Usar una versión publicada hace ≥ 7 días |
| Build rompe por tipo de i18n | Falta una clave en `en.ts` | Completar la paridad de claves |
| Imágenes rotas | URL del proveedor no `https` | Fallback a `/assets/placeholder-product.svg` |

---

Ver también: [`../README.md`](../README.md) · [`../backend/README.md`](../backend/README.md) · [`../AGENTS.md`](../AGENTS.md) · [`.opencode/rules/frontend-seo-i18n.md`](../.opencode/rules/frontend-seo-i18n.md).
