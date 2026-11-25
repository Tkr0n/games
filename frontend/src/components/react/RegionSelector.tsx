import { useId } from 'react';

import type { Locale } from '../../i18n/config';
import { getDictionary } from '../../i18n/utils';
import { ALL_REGIONS, PRIMARY_REGIONS, getRegionMeta } from '../../lib/regions';
import type { RegionCode } from '../../lib/types';

/** Countries whose selection can be widened with Global stock. */
const INCLUDE_GLOBAL_REGIONS: readonly RegionCode[] = ['USA', 'MX', 'PA', 'CO'];

export interface RegionSelection {
  /** `null` means "all regions" (no backend filter). */
  region: RegionCode | null;
  includeGlobal: boolean;
}

interface RegionSelectorProps {
  locale: Locale;
  value: RegionSelection;
  onChange: (next: RegionSelection) => void;
  className?: string;
}

function isCountry(region: RegionCode | null): region is RegionCode {
  return region !== null && INCLUDE_GLOBAL_REGIONS.includes(region);
}

function pillClasses(active: boolean): string {
  const base =
    'rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon';

  return active
    ? `${base} border-verkku-neon bg-verkku-neon text-verkku-bg-deep`
    : `${base} border-verkku-mint-soft/30 bg-verkku-surface text-verkku-stone hover:border-verkku-neon/60 hover:text-verkku-light`;
}

/**
 * Prominent region pills plus the "Include Global" toggle.
 *
 * Selecting a country turns Global stock on by default — that is the flow the
 * design asks for, because most Venezuelan accounts shop in USA/MX/PA/CO and
 * Global cards are the usual fallback.
 */
export default function RegionSelector({ locale, value, onChange, className }: RegionSelectorProps) {
  const t = getDictionary(locale);
  const toggleId = useId();
  const hintId = useId();
  const countrySelected = isCountry(value.region);
  const globalSelected = value.region === 'GLOBAL';

  const select = (region: RegionCode | null): void => {
    onChange({ region, includeGlobal: isCountry(region) ? true : false });
  };

  return (
    <div className={`space-y-3 ${className ?? ''}`}>
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t.catalog.filters.regionLabel}>
        <button
          type="button"
          aria-pressed={value.region === null}
          onClick={() => select(null)}
          className={pillClasses(value.region === null)}
        >
          ALL
        </button>

        {PRIMARY_REGIONS.map((code) => {
          const meta = getRegionMeta(code, locale);
          const active = value.region === code;

          return (
            <button
              key={code}
              type="button"
              aria-pressed={active}
              aria-label={meta.label}
              title={meta.description}
              onClick={() => select(code)}
              className={pillClasses(active)}
            >
              {code}
            </button>
          );
        })}

        {ALL_REGIONS.filter((code) => !PRIMARY_REGIONS.includes(code)).map((code) => {
          const meta = getRegionMeta(code, locale);
          const active = value.region === code;

          return (
            <button
              key={code}
              type="button"
              aria-pressed={active}
              title={meta.description}
              onClick={() => select(code)}
              className={pillClasses(active)}
            >
              {meta.label}
            </button>
          );
        })}
      </div>

      <p className="text-xs leading-relaxed text-verkku-mint-soft">{t.catalog.filters.regionHint}</p>

      {countrySelected ? (
        <div className="flex items-start gap-2">
          <input
            id={toggleId}
            type="checkbox"
            checked={value.includeGlobal}
            onChange={(event) => onChange({ region: value.region, includeGlobal: event.target.checked })}
            aria-describedby={hintId}
            className="mt-0.5 size-4 rounded border-verkku-mint-soft/50 bg-verkku-surface accent-verkku-neon focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-verkku-neon"
          />
          <div className="space-y-0.5">
            <label htmlFor={toggleId} className="text-xs font-semibold text-verkku-stone">
              {t.catalog.filters.includeGlobal}
            </label>
            <p id={hintId} className="text-xs leading-relaxed text-verkku-mint-soft">
              {t.catalog.filters.includeGlobalHint}
            </p>
          </div>
        </div>
      ) : null}

      {globalSelected ? (
        <p className="rounded-lg border border-verkku-mint-soft/25 bg-verkku-surface/60 px-3 py-2 text-xs leading-relaxed text-verkku-stone">
          {t.regions.globalNote}
        </p>
      ) : null}
    </div>
  );
}
