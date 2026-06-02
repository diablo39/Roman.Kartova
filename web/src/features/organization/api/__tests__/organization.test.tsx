import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import {
  orgKeys,
  useOrgProfile,
  useUpdateOrgProfile,
  useLogoUrl,
  useUploadOrgLogo,
  useDeleteOrgLogo,
} from "../organization";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function newQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

function mockApiClient(impl: {
  GET?: ReturnType<typeof vi.fn>;
  POST?: ReturnType<typeof vi.fn>;
  PUT?: ReturnType<typeof vi.fn>;
  DELETE?: ReturnType<typeof vi.fn>;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: impl.POST ?? vi.fn(),
    PUT: impl.PUT ?? vi.fn(),
    DELETE: impl.DELETE ?? vi.fn(),
  } as never);
}

const FULL_PROFILE = {
  id: "o1",
  displayName: "Acme Corp",
  description: null,
  defaultTimeZone: "Europe/Oslo",
  logoEtag: null,
  logoMimeType: null,
  createdAt: "2026-01-01T00:00:00Z",
};

describe("organization hooks", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useAuthMock.mockReset();
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "tok-123" },
    });
  });

  describe("orgKeys", () => {
    it("derives stable query keys", () => {
      expect(orgKeys.profile()).toEqual(["org", "profile"]);
      expect(orgKeys.logoUrl(null)).toEqual(["org", "logo", ""]);
      expect(orgKeys.logoUrl("abc")).toEqual(["org", "logo", "abc"]);
    });
  });

  describe("useOrgProfile", () => {
    it("fetches /api/v1/organizations/me and exposes the response", async () => {
      const get = vi.fn().mockResolvedValue({ data: FULL_PROFILE, error: undefined });
      mockApiClient({ GET: get });

      const { result } = renderHook(() => useOrgProfile(), {
        wrapper: makeWrapper(newQueryClient()),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/me",
        expect.objectContaining({ signal: expect.any(AbortSignal) }),
      );
      expect(result.current.data).toEqual(FULL_PROFILE);
    });

    it("surfaces API errors as query error state", async () => {
      const get = vi.fn().mockResolvedValue({
        data: undefined,
        error: { status: 500, title: "boom" },
      });
      mockApiClient({ GET: get });

      const { result } = renderHook(() => useOrgProfile(), {
        wrapper: makeWrapper(newQueryClient()),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  describe("useUpdateOrgProfile", () => {
    it("PUTs the body without an If-Match header when ifMatch is omitted", async () => {
      const put = vi.fn().mockResolvedValue({
        data: undefined,
        error: undefined,
        response: { status: 204 } as Response,
      });
      mockApiClient({ PUT: put });

      const qc = newQueryClient();
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useUpdateOrgProfile(), { wrapper: makeWrapper(qc) });
      await result.current.mutateAsync({
        displayName: "Acme v2",
        description: "Hello",
        defaultTimeZone: "Europe/Oslo",
      });

      expect(put).toHaveBeenCalledWith(
        "/api/v1/organizations/me",
        expect.objectContaining({
          body: { displayName: "Acme v2", description: "Hello", defaultTimeZone: "Europe/Oslo" },
          headers: undefined,
        }),
      );
      expect(invalidate).toHaveBeenCalledWith({ queryKey: orgKeys.profile() });
    });

    it("PUTs with If-Match header when ifMatch is supplied and strips ifMatch from the body", async () => {
      const put = vi.fn().mockResolvedValue({
        data: undefined,
        error: undefined,
        response: { status: 204 } as Response,
      });
      mockApiClient({ PUT: put });

      const { result } = renderHook(() => useUpdateOrgProfile(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await result.current.mutateAsync({
        displayName: "Acme v2",
        description: null,
        defaultTimeZone: "UTC",
        ifMatch: '"v1"',
      });

      expect(put).toHaveBeenCalledWith(
        "/api/v1/organizations/me",
        expect.objectContaining({
          body: { displayName: "Acme v2", description: null, defaultTimeZone: "UTC" },
          headers: { "If-Match": '"v1"' },
        }),
      );
      // The ifMatch field must not leak into the body.
      const call = put.mock.calls[0];
      expect(call).toBeDefined();
      const options = call![1] as { body: Record<string, unknown> };
      expect(options.body).not.toHaveProperty("ifMatch");
    });

    it("attaches __status on 412 so callers can branch on concurrency conflict", async () => {
      const put = vi.fn().mockResolvedValue({
        data: undefined,
        error: { type: "https://kartova.io/problems/concurrency-conflict", title: "stale" },
        response: { status: 412 } as Response,
      });
      mockApiClient({ PUT: put });

      const { result } = renderHook(() => useUpdateOrgProfile(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await expect(
        result.current.mutateAsync({
          displayName: "X",
          description: null,
          defaultTimeZone: "UTC",
          ifMatch: '"v0"',
        }),
      ).rejects.toMatchObject({ __status: 412 });
    });
  });

  describe("useLogoUrl", () => {
    it("returns null when logoEtag is null on the profile", async () => {
      const get = vi.fn().mockResolvedValue({
        data: { ...FULL_PROFILE, logoEtag: null },
        error: undefined,
      });
      mockApiClient({ GET: get });

      const { result } = renderHook(() => useLogoUrl(), {
        wrapper: makeWrapper(newQueryClient()),
      });

      // First render: profile query is still loading -> data undefined -> null.
      expect(result.current).toBeNull();
      // Wait for the query to settle to confirm logoEtag=null still resolves to null.
      await waitFor(() => expect(result.current).toBeNull());
    });

    it("returns a versioned URL when logoEtag is set", async () => {
      const get = vi.fn().mockResolvedValue({
        data: { ...FULL_PROFILE, logoEtag: "abc123", logoMimeType: "image/png" },
        error: undefined,
      });
      mockApiClient({ GET: get });

      const { result } = renderHook(() => useLogoUrl(), {
        wrapper: makeWrapper(newQueryClient()),
      });

      await waitFor(() =>
        expect(result.current).toBe("/api/v1/organizations/me/logo?v=abc123"),
      );
    });
  });

  describe("useUploadOrgLogo", () => {
    it("sends a PUT with the supplied mime type + bearer token and returns the upload response", async () => {
      const body = { logoEtag: "newhash", mimeType: "image/png" };
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      const qc = newQueryClient();
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useUploadOrgLogo(), { wrapper: makeWrapper(qc) });
      const bytes = new Blob([new Uint8Array([1, 2, 3, 4])], { type: "image/png" });
      const data = await result.current.mutateAsync({ bytes, mimeType: "image/png" });

      expect(data).toEqual(body);
      // The URL must be prefixed with the API base — relative paths hit the
      // SPA dev-server origin (Vite, :5173) instead of the API (:8080) and
      // silently 404, which is exactly the H4 SPA-1 bug this fix closes. We
      // assert against the absolute URL substring to keep the test resilient
      // to environment-specific VITE_API_BASE_URL overrides while still
      // catching the "no prefix at all" regression.
      const [calledUrl, calledInit] = fetchSpy.mock.calls[0]!;
      expect(typeof calledUrl).toBe("string");
      const urlString = calledUrl as string;
      expect(urlString.endsWith("/api/v1/organizations/me/logo")).toBe(true);
      expect(/^https?:\/\//.test(urlString)).toBe(true);
      expect(calledInit).toMatchObject({
        method: "PUT",
        headers: {
          "Content-Type": "image/png",
          Authorization: "Bearer tok-123",
        },
        body: bytes,
      });
      expect(invalidate).toHaveBeenCalledWith({ queryKey: orgKeys.profile() });
    });

    it("targets the API origin (NOT the SPA origin) — H4 SPA-1 regression", async () => {
      // Before the H4 fix, the upload used a relative URL (`/api/v1/...`),
      // which the browser resolved against the SPA dev-server origin
      // (`http://localhost:5173`) instead of the API origin
      // (`http://localhost:8080`), producing a silent 404 with no user-facing
      // error toast. This test pins the fix by asserting the URL starts with
      // an absolute http(s):// prefix matching VITE_API_BASE_URL's default
      // (`http://localhost:8080`) — i.e. the resolved hostname is NOT 5173.
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(
          new Response('{"logoEtag":"e","mimeType":"image/png"}', {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }),
        );

      const { result } = renderHook(() => useUploadOrgLogo(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await result.current.mutateAsync({
        bytes: new Blob([new Uint8Array([1])]),
        mimeType: "image/png",
      });

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const calledUrl = fetchSpy.mock.calls[0]![0] as string;
      // Must be absolute and end with the API path — kills the "no prefix"
      // mutant and the "prefix points at SPA origin" mutant. Parsing via URL
      // catches both shapes deterministically.
      const parsed = new URL(calledUrl);
      expect(parsed.pathname).toBe("/api/v1/organizations/me/logo");
      // The default API origin in dev is 8080; the SPA origin is 5173. The
      // test environment's VITE_API_BASE_URL is unset, so the default kicks
      // in — assert the port is 8080 to lock in the API target.
      expect(parsed.port).toBe("8080");
    });

    it("throws when the OIDC user has no access_token", async () => {
      useAuthMock.mockReturnValue({ isAuthenticated: false, user: undefined });
      const fetchSpy = vi.spyOn(globalThis, "fetch");

      const { result } = renderHook(() => useUploadOrgLogo(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await expect(
        result.current.mutateAsync({
          bytes: new Blob([new Uint8Array([0])]),
          mimeType: "image/png",
        }),
      ).rejects.toThrow(/not authenticated/i);
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it("throws with __status attached when the server returns a non-2xx", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response('{"title":"too big"}', { status: 413 }),
      );

      const { result } = renderHook(() => useUploadOrgLogo(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await expect(
        result.current.mutateAsync({
          bytes: new Blob([new Uint8Array([0])]),
          mimeType: "image/png",
        }),
      ).rejects.toMatchObject({ __status: 413 });
    });
  });

  describe("useDeleteOrgLogo", () => {
    it("calls DELETE and invalidates the profile query on success", async () => {
      const del = vi.fn().mockResolvedValue({
        data: undefined,
        error: undefined,
        response: { status: 204 } as Response,
      });
      mockApiClient({ DELETE: del });

      const qc = newQueryClient();
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useDeleteOrgLogo(), { wrapper: makeWrapper(qc) });
      await result.current.mutateAsync();

      expect(del).toHaveBeenCalledWith("/api/v1/organizations/me/logo", {});
      expect(invalidate).toHaveBeenCalledWith({ queryKey: orgKeys.profile() });
    });

    it("attaches __status when the server returns an error (e.g. 404)", async () => {
      const del = vi.fn().mockResolvedValue({
        data: undefined,
        error: { title: "Not Found" },
        response: { status: 404 } as Response,
      });
      mockApiClient({ DELETE: del });

      const { result } = renderHook(() => useDeleteOrgLogo(), {
        wrapper: makeWrapper(newQueryClient()),
      });
      await expect(result.current.mutateAsync()).rejects.toMatchObject({ __status: 404 });
    });
  });
});
