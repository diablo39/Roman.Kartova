import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createAuthedApiClient } from "../createAuthedApiClient";
import { isRecentReauthAttempt, markReauthAttempt } from "@/shared/oidc/reauthMarker";

function jsonResponse(status: number, body: unknown = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

beforeEach(() => window.sessionStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe("createAuthedApiClient", () => {
  it("sends the current bearer token", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse(200));
    const client = createAuthedApiClient("http://api.test", () => "tok-1", vi.fn());

    await client.GET("/api/v1/admin/session/me");

    const request = fetchSpy.mock.calls[0]![0] as Request;
    expect(request.headers.get("Authorization")).toBe("Bearer tok-1");
    expect(request.url).toBe("http://api.test/api/v1/admin/session/me");
  });

  it("sends no Authorization header when there is no token", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse(200));
    const client = createAuthedApiClient("http://api.test", () => null, vi.fn());

    await client.GET("/api/v1/admin/session/me");

    expect((fetchSpy.mock.calls[0]![0] as Request).headers.has("Authorization")).toBe(false);
  });

  it("reads the token lazily on every request", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockImplementation(async () => jsonResponse(200));
    let token = "first";
    const client = createAuthedApiClient("http://api.test", () => token, vi.fn());

    await client.GET("/api/v1/admin/session/me");
    token = "second";
    await client.GET("/api/v1/admin/session/me");

    expect((fetchSpy.mock.calls[1]![0] as Request).headers.get("Authorization")).toBe("Bearer second");
  });

  it("calls onUnauthorized on 401 only", async () => {
    const onUnauthorized = vi.fn();
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const client = createAuthedApiClient("http://api.test", () => "t", onUnauthorized);

    fetchSpy.mockResolvedValueOnce(jsonResponse(403));
    await client.GET("/api/v1/admin/session/me");
    expect(onUnauthorized).not.toHaveBeenCalled();

    fetchSpy.mockResolvedValueOnce(jsonResponse(401));
    await client.GET("/api/v1/admin/session/me");
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it("calls onUnauthorized with hadToken: false when the request carried no token", async () => {
    const onUnauthorized = vi.fn();
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse(401));
    const client = createAuthedApiClient("http://api.test", () => null, onUnauthorized);

    await client.GET("/api/v1/admin/session/me");

    expect(onUnauthorized).toHaveBeenCalledWith({ hadToken: false });
  });

  it("calls onUnauthorized with hadToken: true when the request carried a token", async () => {
    const onUnauthorized = vi.fn();
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse(401));
    const client = createAuthedApiClient("http://api.test", () => "t", onUnauthorized);

    await client.GET("/api/v1/admin/session/me");

    expect(onUnauthorized).toHaveBeenCalledWith({ hadToken: true });
  });

  it("an authenticated non-401 response clears the re-auth marker (403 counts: the token was accepted)", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const client = createAuthedApiClient("http://api.test", () => "t", vi.fn());

    markReauthAttempt();
    fetchSpy.mockResolvedValueOnce(jsonResponse(200));
    await client.GET("/api/v1/admin/session/me");
    expect(isRecentReauthAttempt()).toBe(false);

    markReauthAttempt();
    fetchSpy.mockResolvedValueOnce(jsonResponse(403));
    await client.GET("/api/v1/admin/session/me");
    expect(isRecentReauthAttempt()).toBe(false);
  });

  it("keeps the re-auth marker on a 401 and on an anonymous (tokenless) success", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    markReauthAttempt();
    fetchSpy.mockResolvedValueOnce(jsonResponse(401));
    await createAuthedApiClient("http://api.test", () => "t", vi.fn()).GET("/api/v1/admin/session/me");
    expect(isRecentReauthAttempt()).toBe(true);

    fetchSpy.mockResolvedValueOnce(jsonResponse(200));
    await createAuthedApiClient("http://api.test", () => null, vi.fn()).GET("/api/v1/admin/session/me");
    expect(isRecentReauthAttempt()).toBe(true);
  });
});
