import { defineConfig } from 'astro/config';
import cloudflare from '@astrojs/cloudflare';
import react from '@astrojs/react';
import tailwindcss from '@tailwindcss/vite';

const siteUrl = process.env.PUBLIC_SITE_URL?.trim() || 'https://games.verkku.com';

export default defineConfig({
  site: siteUrl,
  output: 'server',
  adapter: cloudflare({
    imageService: 'passthrough',
  }),
  integrations: [react()],
  i18n: {
    locales: ['es', 'en'],
    defaultLocale: 'es',
    routing: {
      prefixDefaultLocale: true,
    },
  },
  security: {
    checkOrigin: true,
  },
  server: {
    // Dev-only: allow Cloudflare quick-tunnel hostnames (and anything else the
    // dev server proxies) to reach the Vite dev server. Never affects the
    // production Cloudflare Workers build.
    allowedHosts: true,
  },
  vite: {
    plugins: [tailwindcss()],
  },
});
