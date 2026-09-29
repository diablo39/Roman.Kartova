import createClient from "openapi-fetch";
import type { paths } from "@/generated/openapi";
import { createAuthedApiClient, deferredFetch } from "@/shared/api/createAuthedApiClient";
import { resolveConfigValue } from "@/shared/config/runtimeConfig";

type TokenProvider = () => string | null;
let tokenProvider: TokenProvider = () => null;

export function setAccessTokenProvider(p: TokenProvider): void {
  tokenProvider = p;
}

let unauthorizedHandler: () => void = () => {};

export function setUnauthorizedHandler(h: () => void): void {
  unauthorizedHandler = h;
}

export function createApiClient(baseUrl: string) {
  return createAuthedApiClient(baseUrl, () => tokenProvider(), () => unauthorizedHandler());
}

export function createAnonymousApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl, fetch: deferredFetch });
}

/**
 * Single source of truth for the SPA's API origin — runtime `/config.js` (TD-016), then
 * `VITE_API_BASE_URL`, then the `docker compose up` default (`http://localhost:8080`). Always an
 * absolute origin: the API is served from its own host.
 *
 * Exported so non-openapi-fetch call sites (raw `fetch` for binary uploads, test fixtures asserting
 * URL composition) compose absolute URLs from the same value — no config reads outside this file.
 */
export const API_BASE_URL: string = resolveConfigValue(
  "apiBaseUrl",
  import.meta.env.VITE_API_BASE_URL,
  "http://localhost:8080",
);

export const apiClient = createApiClient(API_BASE_URL);
