/**
 * Circuit breaker for the 401 → signinRedirect loop (TD-017). The 401 handler marks an attempt right
 * before redirecting; any authenticated non-401 response clears it. A 401 while the mark is younger than
 * REAUTH_LOOP_WINDOW_MS means SSO came back but the API still rejects the session, so redirecting again
 * would loop. sessionStorage is per-origin, so the tenant SPA and the admin console never share a mark.
 * Storage failures (privacy mode, quota) read as "no mark", which keeps the pre-breaker behaviour.
 */
const KEY = "kartova.reauth-attempt";

export const REAUTH_LOOP_WINDOW_MS = 30_000;

export function markReauthAttempt(now: number = Date.now()): void {
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify({ ts: now }));
  } catch {
    // storage unavailable → breaker disabled
  }
}

export function isRecentReauthAttempt(now: number = Date.now()): boolean {
  try {
    const raw = window.sessionStorage.getItem(KEY);
    if (!raw) return false;
    const ts: unknown = (JSON.parse(raw) as { ts?: unknown }).ts;
    if (typeof ts !== "number") return false;
    const age = now - ts;
    return age >= 0 && age < REAUTH_LOOP_WINDOW_MS;
  } catch {
    return false;
  }
}

export function clearReauthMarker(): void {
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {
    // storage unavailable → nothing to clear
  }
}
