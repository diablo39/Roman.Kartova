import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";

const getMock = vi.fn();
vi.mock("../client", () => ({
  apiClient: { GET: (...a: unknown[]) => getMock(...a) },
  API_BASE_URL: "http://localhost:8080",
}));

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { useApisList, useApiSpec, useUpsertApiSpec } from "../apis";

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  vi.restoreAllMocks();
  getMock.mockReset();
  getMock.mockResolvedValue({ data: { items: [], nextCursor: null, prevCursor: null }, error: undefined });
  useAuthMock.mockReset();
  useAuthMock.mockReturnValue({ isAuthenticated: true, user: { access_token: "test-token" } });
});

describe("useApisList", () => {
  it("passes style/teamId/displayNameContains only when non-empty", async () => {
    renderHook(() => useApisList({
      sortBy: "displayName", sortOrder: "asc",
      style: ["rest"], teamId: ["t1"], displayNameContains: "ord",
    }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0]?.[1].params.query;
    expect(query.style).toEqual(["rest"]);
    expect(query.teamId).toEqual(["t1"]);
    expect(query.displayNameContains).toBe("ord");
  });

  it("omits empty filter arrays", async () => {
    renderHook(() => useApisList({ sortBy: "displayName", sortOrder: "asc", style: [], teamId: [] }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0]?.[1].params.query;
    expect(query.style).toBeUndefined();
    expect(query.teamId).toBeUndefined();
    expect(query.displayNameContains).toBeUndefined();
  });
});

describe("useApiSpec / useUpsertApiSpec", () => {
  it("useApiSpec GETs raw spec and returns content + mediaType", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("channels: {}", { status: 200, headers: { "Content-Type": "application/yaml" } }),
    );
    const { result } = renderHook(() => useApiSpec("api-1", true), { wrapper });
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringContaining("/api/v1/catalog/apis/api-1/spec"),
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: "Bearer test-token" }) }),
    );
    expect(result.current.data).toEqual({ content: "channels: {}", mediaType: "application/yaml" });
  });

  it("useApiSpec returns null on 404 (no spec yet)", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 404 }));
    const { result } = renderHook(() => useApiSpec("api-1", true), { wrapper });
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data).toBeNull();
  });

  it("useApiSpec does not fetch when hasSpec is false", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    renderHook(() => useApiSpec("api-1", false), { wrapper });
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("useUpsertApiSpec PUTs raw body with chosen Content-Type", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 201 }));
    const { result } = renderHook(() => useUpsertApiSpec("api-1"), { wrapper });
    await result.current.mutateAsync({ content: "{}", mediaType: "application/json" });
    expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringContaining("/api/v1/catalog/apis/api-1/spec"),
      expect.objectContaining({
        method: "PUT",
        headers: expect.objectContaining({ "Content-Type": "application/json", Authorization: "Bearer test-token" }),
        body: "{}",
      }),
    );
  });
});
