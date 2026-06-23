/** Routes that are part of the auth handshake — never a useful place to land. */
const AUTH_ROUTES = new Set([
  "/callback",
  "/login-error",
  "/welcome",
  "/accept-invitation",
]);

// Relative path that does not start a cross-origin reference: leading "/" not
// followed by another "/" or a "\" (the WHATWG URL parser treats "\" as "/"
// for http(s), so "/\evil.com" would resolve cross-origin — an open redirect).
const SAME_ORIGIN_PATH = /^\/(?![/\\])/;

// Control chars (NUL/TAB/CR/LF … and DEL) have no place in a path and can
// smuggle a cross-origin reference past naive checks; reject any. Done with a
// code-point scan rather than a regex so no control byte sits in source.
function hasControlChar(s: string): boolean {
  for (let i = 0; i < s.length; i++) {
    const code = s.charCodeAt(i);
    if (code <= 0x1f || code === 0x7f) return true;
  }
  return false;
}

/**
 * Pull a safe return-to path out of the OIDC `state` round-tripped from
 * `RequireAuth` (which stashes the deep link the user requested before the
 * login redirect; the value is therefore attacker-influenceable — any deep
 * link a logged-out victim visits seeds it).
 *
 * Accepts only same-origin **relative** paths (see `SAME_ORIGIN_PATH`), with no
 * control chars, and excludes the auth-flow routes (case-insensitively) so we
 * never bounce back into the login round-trip.
 *
 * Returns `undefined` for anything unusable, leaving the caller to fall back to
 * its default (`/catalog`). A *present-but-rejected* value is `console.warn`-ed:
 * silence there would let a blocked open-redirect — or a silent regression of
 * the round-trip back to always-`/catalog` (the very bug this restores) — pass
 * unnoticed. A genuinely absent value (no deep link to restore) is silent.
 */
export function resolveReturnTo(state: unknown): string | undefined {
  const candidate = (state as { returnTo?: unknown } | undefined)?.returnTo;
  if (candidate === undefined) return undefined; // no deep link to restore — expected

  const safe =
    typeof candidate === "string" &&
    SAME_ORIGIN_PATH.test(candidate) &&
    !hasControlChar(candidate) &&
    !AUTH_ROUTES.has((candidate.split(/[?#]/, 1)[0] ?? "").toLowerCase());

  if (!safe) {
    console.warn("resolveReturnTo: ignoring unusable returnTo", candidate);
    return undefined;
  }
  return candidate;
}
