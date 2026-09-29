import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "@/generated/openapi";

/**
 * Wrap globalThis.fetch so test spies installed after the client is created are honoured.
 * openapi-fetch captures the fetch reference at createClient() time; this indirection
 * ensures the call is always dispatched through the live globalThis.fetch binding.
 */
export const deferredFetch: typeof fetch = (input, init) => globalThis.fetch(input as Request, init);

/**
 * openapi-fetch client that sends `Authorization: Bearer <getToken()>` (read per request) and calls
 * `onUnauthorized` on any 401. Shared by the tenant SPA and the admin console. Each app owns its
 * token source and 401 handler, because each has its own OIDC session on its own origin (ADR-0118).
 */
export function createAuthedApiClient(
  baseUrl: string,
  getToken: () => string | null,
  onUnauthorized: () => void,
) {
  const authMiddleware: Middleware = {
    async onRequest({ request }) {
      const tok = getToken();
      if (tok) request.headers.set("Authorization", `Bearer ${tok}`);
      return request;
    },
    async onResponse({ response }) {
      if (response.status === 401) onUnauthorized();
      return response;
    },
  };
  const client = createClient<paths>({ baseUrl, fetch: deferredFetch });
  client.use(authMiddleware);
  return client;
}
