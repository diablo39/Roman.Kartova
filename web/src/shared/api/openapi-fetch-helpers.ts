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
 *   return unwrapData(data);
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
 * `data` MUST be present. Throws a generic error if both are absent —
 * indicates a contract violation upstream (e.g. a middleware swallowed
 * the body) rather than a real failure mode the UI should branch on.
 */
export function unwrapData<T>(data: T | undefined): T {
  if (!data) throw new Error("API returned neither data nor error");
  return data;
}
