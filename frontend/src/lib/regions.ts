import type { Locale } from '../i18n/config';
import type { RegionCode } from './types';

/** Regions with real storefront coverage first; `OTHER` is the catch-all. */
export const PRIMARY_REGIONS: readonly RegionCode[] = ['USA', 'GLOBAL', 'MX', 'PA', 'CO'];

export const ALL_REGIONS: readonly RegionCode[] = [...PRIMARY_REGIONS, 'OTHER'];

export interface RegionMeta {
  code: RegionCode;
  label: string;
  shortLabel: string;
  badgeTone: string;
  description: string;
}

interface RegionMetaSource {
  label: Record<Locale, string>;
  shortLabel: Record<Locale, string>;
  /** Semantic tone token consumed by the badge component classes. */
  badgeTone: string;
  description: Record<Locale, string>;
}

const REGION_META: Record<RegionCode, RegionMetaSource> = {
  USA: {
    label: { es: 'Estados Unidos', en: 'United States' },
    shortLabel: { es: 'USA', en: 'USA' },
    badgeTone: 'emerald',
    description: {
      es: 'Tarjetas y recargas para cuentas con tienda de Estados Unidos. Los códigos USA no canjean en cuentas de otra región.',
      en: 'Cards and top-ups for accounts on the United States store. USA codes will not redeem on accounts from other regions.',
    },
  },
  GLOBAL: {
    label: { es: 'Global', en: 'Global' },
    shortLabel: { es: 'Global', en: 'Global' },
    badgeTone: 'mint',
    description: {
      es: 'Códigos Global que sirven en varias tiendas. Revisa que tu plataforma los acepte en tu cuenta antes de comprar.',
      en: 'Global codes that work across several stores. Check that your platform accepts them on your account before buying.',
    },
  },
  MX: {
    label: { es: 'México', en: 'Mexico' },
    shortLabel: { es: 'MX', en: 'MX' },
    badgeTone: 'teal',
    description: {
      es: 'Para cuentas con tienda de México. Ideal si tu perfil de PSN o Xbox está configurado en MXN.',
      en: 'For accounts on the Mexican store. Ideal when your PSN or Xbox profile is set to MXN.',
    },
  },
  PA: {
    label: { es: 'Panamá', en: 'Panama' },
    shortLabel: { es: 'PA', en: 'PA' },
    badgeTone: 'mint-soft',
    description: {
      es: 'Para cuentas con tienda de Panamá. Confirma la región de tu perfil antes de pagar.',
      en: 'For accounts on the Panamanian store. Confirm your profile region before paying.',
    },
  },
  CO: {
    label: { es: 'Colombia', en: 'Colombia' },
    shortLabel: { es: 'CO', en: 'CO' },
    badgeTone: 'teal-bright',
    description: {
      es: 'Para cuentas con tienda de Colombia. Traemos recargas y gift cards en COP.',
      en: 'For accounts on the Colombian store. Top-ups and gift cards in COP.',
    },
  },
  OTHER: {
    label: { es: 'Otra región', en: 'Other region' },
    shortLabel: { es: 'Otra', en: 'Other' },
    badgeTone: 'stone',
    description: {
      es: 'Otras regiones que traemos bajo pedido. Escríbenos y la buscamos contigo.',
      en: 'Other regions available on request. Message us and we will track it down with you.',
    },
  },
};

export function isValidRegionCode(value: string): value is RegionCode {
  return (ALL_REGIONS as readonly string[]).includes(value);
}

export function getRegionMeta(code: RegionCode, locale: Locale): RegionMeta {
  const meta = REGION_META[code];

  return {
    code,
    label: meta.label[locale],
    shortLabel: meta.shortLabel[locale],
    badgeTone: meta.badgeTone,
    description: meta.description[locale],
  };
}
