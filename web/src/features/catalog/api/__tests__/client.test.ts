import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  createApiClient,
  setAccessTokenProvider,
  setUnauthorizedHandler,
} from "../client";

function makeFetchSpy(status = 200, body = "[]"): {
  spy: ReturnType<typeof vi.fn>;
  lastRequestHeaders: () => Headers | undefined;
} {
  let lastHeaders: Headers | undefined;
  const spy = vi.fn(async (input: Request | string | URL, init?: RequestInit) => {
    if (input instanceof Request) {
      lastHeaders = input.headers;
    } else {
      lastHeaders = new Headers(init?.headers);
    }
    return new Response(body, {
      status,
      headers: { "Content-Type": "application/json" },
    });
  });
  return { spy, lastRequestHeaders: () => lastHeaders };
}

describe("api client middleware", () => {
  let originalFetch: typeof globalThis.fetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
    setAccessTokenProvider(() => null);
    setUnauthorizedHandler(() => {});
  });

  it("attaches Bearer header from provider on API requests", async () => {
    setAccessTokenProvider(() => "tok-123");
    const client = createApiClient("http://api.test");
    const { spy, lastRequestHeaders } = makeFetchSpy();
    globalThis.fetch = spy as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(lastRequestHeaders()?.get("Authorization")).toBe("Bearer tok-123");
  });

  it("does not attach Authorization when token provider returns null", async () => {
    setAccessTokenProvider(() => null);
    const client = createApiClient("http://api.test");
    const { spy, lastRequestHeaders } = makeFetchSpy();
    globalThis.fetch = spy as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(lastRequestHeaders()?.has("Authorization")).toBe(false);
  });

  it("invokes the unauthorized handler on 401", async () => {
    const onUnauthorized = vi.fn();
    setAccessTokenProvider(() => "tok");
    setUnauthorizedHandler(onUnauthorized);
    const client = createApiClient("http://api.test");
    const { spy } = makeFetchSpy(401, "{\"title\":\"unauth\"}");
    globalThis.fetch = spy as unknown as typeof fetch;

    await client.GET("/api/v1/catalog/applications");

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it("preserves Content-Type when sending a JSON body", async () => {
    setAccessTokenProvider(() => "tok");
    const client = createApiClient("http://api.test");
    const { spy, lastRequestHeaders } = makeFetchSpy(201, "{}");
    globalThis.fetch = spy as unknown as typeof fetch;

    // POST with body — openapi-fetch sets Content-Type: application/json automatically.
    await client.POST("/api/v1/catalog/applications", {
      body: { name: "x", displayName: "X" } as never,
    });

    const headers = lastRequestHeaders();
    expect(headers?.get("Content-Type")).toContain("application/json");
    expect(headers?.get("Authorization")).toBe("Bearer tok");
  });
});
