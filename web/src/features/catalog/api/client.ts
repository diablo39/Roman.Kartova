import createClient from "openapi-fetch";
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

// Use a lazy fetch wrapper so that:
//  1. Test mocks replacing globalThis.fetch after client creation are honoured
//     (openapi-fetch captures the fetch reference at createClient() time).
//  2. The Authorization header is passed in the `init` argument (not only baked
//     into the Request object), so test mocks that inspect `init?.headers` see it.
//  3. 401 responses trigger the unauthorizedHandler.
const lazyFetch: typeof fetch = async (input, init) => {
  const tok = tokenProvider();
  const headers = new Headers(
    input instanceof Request ? input.headers : init?.headers
  );
  if (tok) {
    headers.set("Authorization", `Bearer ${tok}`);
  }
  const response = await globalThis.fetch(input, { ...init, headers });
  if (response.status === 401) unauthorizedHandler();
  return response;
};

export function createApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl, fetch: lazyFetch });
}

export const apiClient = createApiClient(
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080"
);
