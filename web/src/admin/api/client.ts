import { createAuthedApiClient } from "@/shared/api/createAuthedApiClient";

type TokenProvider = () => string | null;
let tokenProvider: TokenProvider = () => null;
let unauthorizedHandler: () => void = () => {};

/** Installed by AdminApiAuthBridge — the operator session's live access token. */
export function setAdminAccessTokenProvider(p: TokenProvider): void {
  tokenProvider = p;
}

/** Installed by AdminApiAuthBridge — re-authenticates against kartova-platform on any 401. */
export function setAdminUnauthorizedHandler(h: () => void): void {
  unauthorizedHandler = h;
}

/**
 * The admin console always runs on its own origin (ADR-0118), so the API is always cross-origin
 * (CORS policy KartovaAdminWeb). Build-time value; runtime config is TD-016.
 */
export const ADMIN_API_BASE_URL: string =
  import.meta.env.VITE_ADMIN_API_BASE_URL ?? "http://localhost:8080";

export const adminApiClient = createAuthedApiClient(
  ADMIN_API_BASE_URL,
  () => tokenProvider(),
  () => unauthorizedHandler(),
);
