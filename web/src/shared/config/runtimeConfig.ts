/**
 * Runtime SPA config (TD-016). nginx renders `/config.js` from the container's KARTOVA_* env at start
 * (web/default.conf.template, web/admin.conf.template); index.html / admin.html load it with a plain
 * `<script>` before the bundle, so module-level reads below already see it. One image can then be
 * promoted across environments. In vite dev/preview `public/config.js` sets `{}` and VITE_* apply.
 * In a production build, a fallback (neither runtime nor VITE_* set) logs a one-time console.warn per key.
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

const ENV_VAR_BY_KEY: Record<keyof RuntimeConfig, string> = {
  oidcAuthority: "KARTOVA_OIDC_AUTHORITY",
  oidcClientId: "KARTOVA_OIDC_CLIENT_ID",
  apiBaseUrl: "KARTOVA_API_BASE_URL",
};

const warnedKeys = new Set<string>();

/** Test-only: clears the per-key warned-once state so tests don't leak across cases. */
export function resetRuntimeConfigWarningsForTests(): void {
  warnedKeys.clear();
}

/**
 * First non-empty of: runtime value → build-time VITE_* value → fallback. Empty counts as unset
 * because the image defaults every KARTOVA_* env to "" (envsubst substitutes only defined vars).
 * In a production build, falling all the way through to `fallback` logs a one-time console.warn
 * per key — never throws (spec D2: no fail-hard) — so a Helm deploy that forgets to set the
 * container's KARTOVA_* env, or a `/config.js` that fails to load, leaves a visible signal.
 */
export function resolveConfigValue(
  key: keyof RuntimeConfig,
  viteValue: string | undefined,
  fallback: string,
): string {
  const runtimeValue = window.__KARTOVA_CONFIG__?.[key];
  if (runtimeValue) return runtimeValue;
  if (viteValue) return viteValue;
  if (import.meta.env.PROD && !warnedKeys.has(key)) {
    warnedKeys.add(key);
    console.warn(
      `Kartova runtime config: "${key}" not set — falling back to ${fallback}. ` +
        `Set ${ENV_VAR_BY_KEY[key]} (compose/Helm) — see deploy/README.md.`,
    );
  }
  return fallback;
}
