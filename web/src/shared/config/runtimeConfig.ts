/**
 * Runtime SPA config (TD-016). nginx renders `/config.js` from the container's KARTOVA_* env at start
 * (web/default.conf.template, web/admin.conf.template); index.html / admin.html load it with a plain
 * `<script>` before the bundle, so module-level reads below already see it. One image can then be
 * promoted across environments. In vite dev/preview `public/config.js` sets `{}` and VITE_* apply.
 */
export interface RuntimeConfig {
  oidcAuthority?: string;
  oidcClientId?: string;
  apiBaseUrl?: string;
}

declare global {
  interface Window {
    __KARTOVA_CONFIG__?: RuntimeConfig;
  }
}

/**
 * First non-empty of: runtime value → build-time VITE_* value → fallback. Empty counts as unset
 * because the image defaults every KARTOVA_* env to "" (envsubst substitutes only defined vars).
 */
export function resolveConfigValue(
  key: keyof RuntimeConfig,
  viteValue: string | undefined,
  fallback: string,
): string {
  const runtimeValue = window.__KARTOVA_CONFIG__?.[key];
  if (runtimeValue) return runtimeValue;
  if (viteValue) return viteValue;
  return fallback;
}
