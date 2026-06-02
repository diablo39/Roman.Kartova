import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "@/generated/openapi";

type TokenProvider = () => string | null;
let tokenProvider: TokenProvider = () => null;

export function setAccessTokenProvider(p: TokenProvider): void {
  tokenProvider = p;
}

let unauthorizedHandler: () => void = () => {};

export function setUnauthorizedHandler(h: () => void): void {
  unauthorizedHandler = h;
}

const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const tok = tokenProvider();
    if (tok) request.headers.set("Authorization", `Bearer ${tok}`);
    return request;
  },
  async onResponse({ response }) {
    if (response.status === 401) unauthorizedHandler();
    return response;
  },
};

// Wrap globalThis.fetch so test spies installed after createApiClient() are honoured.
// openapi-fetch captures the fetch reference at createClient() time; this indirection
// ensures the call is always dispatched through the live globalThis.fetch binding.
const deferredFetch: typeof fetch = (input, init) => globalThis.fetch(input as Request, init);

export function createApiClient(baseUrl: string) {
  const client = createClient<paths>({ baseUrl, fetch: deferredFetch });
  client.use(authMiddleware);
  return client;
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
