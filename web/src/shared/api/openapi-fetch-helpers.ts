/**
 * Shared helpers for openapi-fetch call sites across all SPA features.
 *
 * Six feature modules (auth, catalog/applications, organization/organization,
 * organization/invitations, teams, users) each previously declared identical
 * inline copies of these two helpers. Centralized here so the wire-error
 * envelope (`__status` attached for status-aware branching) and the
 * "data must be present when error is absent" invariant are defined once.
 */

/**
 * Re-throws an openapi-fetch error after attaching the HTTP status as a
 * `__status` field so callers can branch on 412 / 409 / 400 / 502 without
 * re-parsing the response.
 *
 * Example:
 *   const { data, error, response } = await apiClient.PUT(...);
 *   if (error) throwWithStatus(error, response);
 *   return unwrapData(data, response);
 */
export function throwWithStatus(
  error: unknown,
  response: { status: number },
): never {
  (error as Record<string, unknown>).__status = response.status;
  throw error;
}

/**
 * Asserts the openapi-fetch happy-path invariant: when `error` is absent,
 * `data` MUST be present.
 *
 * When `data` is missing AND the response itself reports a real HTTP
 * failure (`!response.ok`), that is not a contract violation — it is
 * openapi-fetch's documented behaviour for a body-less error response (e.g.
 * ASP.NET's `Results.Forbid()`, which sends `Content-Length: 0`): `error`
 * comes back `undefined` alongside `data: undefined`. Without `response`
 * here, that case fell through to the generic message below with no status
 * attached, so a `byStatus` map (e.g. the 403 branch in
 * `AddSystemMemberDialog`) could never see it. Synthesize a problem-shaped
 * error carrying the real `__status` via `throwWithStatus` so every
 * existing and future `byStatus`/`byProblemType` dispatch table sees the
 * truth instead of a status-less plain Error.
 *
 * A `response.ok` (or no `response` passed at all — some call sites don't
 * have one in scope) still throws the generic error: that is a genuinely
 * unexpected empty *success*, a contract violation upstream (e.g. a
 * middleware swallowed the body) rather than a real failure mode the UI
 * should branch on.
 */
export function unwrapData<T>(
  data: T | undefined,
  response?: { readonly ok: boolean; readonly status: number },
): T {
  if (data) return data;
  if (response && !response.ok) {
    throwWithStatus(
      { title: `Request failed with status ${response.status}` },
      response,
    );
  }
  throw new Error("API returned neither data nor error");
}
