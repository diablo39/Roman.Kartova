import createClient from "openapi-fetch";
import type { paths } from "@/generated/openapi";
import { createAuthedApiClient, deferredFetch } from "@/shared/api/createAuthedApiClient";

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
 * Single source of truth for the SPA's API origin. The default
 * (`http://localhost:8080`) lines up with `docker compose up`'s API origin;
 * production deployments collapse SPA and API to the same host so
 * `VITE_API_BASE_URL` is typically unset and relative paths Just Work.
 *
 * Exported so non-openapi-fetch call sites (raw `fetch` for binary uploads,
 * test fixtures asserting URL composition) can compose absolute URLs from
 * the same env read — no `import.meta.env.VITE_API_BASE_URL` duplication
 * outside this file.
 */
export const API_BASE_URL: string =
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export const apiClient = createApiClient(API_BASE_URL);
