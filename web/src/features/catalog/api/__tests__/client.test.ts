import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  createApiClient,
  setAccessTokenProvider,
  setUnauthorizedHandler,
} from "../client";

describe("api client middleware", () => {
  let originalFetch: typeof globalThis.fetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
    // Reset module-level singletons.
    setAccessTokenProvider(() => null);
    setUnauthorizedHandler(() => {});
  });

  it("attaches Bearer header from provider on API requests", async () => {
    setAccessTokenProvider(() => "tok-123");
    const client = createApiClient("http://api.test");
    const captured: { headers?: Headers } = {};
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      captured.headers = new Headers(init?.headers);
      return new Response("[]", {
        status: 200, headers: { "Content-Type": "application/json" },
      });
    }) as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(captured.headers!.get("Authorization")).toBe("Bearer tok-123");
  });

  it("does not attach Authorization when token provider returns null", async () => {
    setAccessTokenProvider(() => null);
    const client = createApiClient("http://api.test");
    const captured: { headers?: Headers } = {};
    globalThis.fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      captured.headers = new Headers(init?.headers);
      return new Response("[]", {
        status: 200, headers: { "Content-Type": "application/json" },
      });
    }) as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(captured.headers!.has("Authorization")).toBe(false);
  });

  it("invokes the unauthorized handler on 401", async () => {
    const onUnauthorized = vi.fn();
    setAccessTokenProvider(() => "tok");
    setUnauthorizedHandler(onUnauthorized);
    const client = createApiClient("http://api.test");
    globalThis.fetch = vi.fn(async () =>
      new Response("{\"title\":\"unauth\"}", {
        status: 401, headers: { "Content-Type": "application/problem+json" },
      })
    ) as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
