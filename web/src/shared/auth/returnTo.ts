/**
 * Pull a safe return-to path out of the OIDC `state` round-tripped from
 * `RequireAuth` (which stashes the deep link the user requested before the
 * login redirect). Accepts only same-origin **relative** paths (`/…`) —
 * rejects protocol-relative (`//host`) and absolute URLs to avoid an open
 * redirect — and skips the auth-flow routes themselves so we never bounce
 * back into the login round-trip. Anything else returns `undefined`, leaving
 * the caller to fall back to its default (`/catalog`).
 */
export function resolveReturnTo(state: unknown): string | undefined {
  const candidate = (state as { returnTo?: unknown } | undefined)?.returnTo;
  if (typeof candidate !== "string") return undefined;
  if (!candidate.startsWith("/") || candidate.startsWith("//")) return undefined;
  const path = candidate.split(/[?#]/, 1)[0];
  if (path === "/callback" || path === "/login-error" || path === "/welcome") {
    return undefined;
  }
  return candidate;
}
