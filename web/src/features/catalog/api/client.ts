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

export const apiClient = createApiClient(
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080"
);
