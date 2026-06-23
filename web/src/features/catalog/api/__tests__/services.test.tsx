import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import * as clientModule from "@/features/catalog/api/client";
import { useService, useServicesList, useRegisterService } from "../services";

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe("services api", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("useService GETs by path id", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { id: "svc-1", tenantId: "t", displayName: "Orders", description: "d", teamId: "tm",
        createdByUserId: "u", createdAt: "2026-01-01T00:00:00Z", health: "unknown", endpoints: [], version: "v1" },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(() => useService("svc-1"), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.data?.displayName).toBe("Orders"));
    expect(get).toHaveBeenCalledWith("/api/v1/catalog/services/{id}", { params: { path: { id: "svc-1" } } });
  });

  it("useServicesList GETs the list with sort params", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { items: [], nextCursor: null, prevCursor: null }, error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(
      () => useServicesList({ sortBy: "displayName", sortOrder: "desc" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(get).toHaveBeenCalledWith("/api/v1/catalog/services", expect.objectContaining({
      params: { query: expect.objectContaining({ sortBy: "displayName", sortOrder: "desc" }) },
    }));
  });

  it("useRegisterService POSTs the body", async () => {
    const post = vi.fn().mockResolvedValue({ data: { id: "svc-1" }, error: undefined, response: { status: 201 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);

    const { result } = renderHook(() => useRegisterService(), { wrapper: wrapper() });
    await result.current.mutateAsync({ displayName: "Orders", description: "d", teamId: "tm", endpoints: [] });
    expect(post).toHaveBeenCalledWith("/api/v1/catalog/services", {
      body: { displayName: "Orders", description: "d", teamId: "tm", endpoints: [] },
    });
  });

  it("sends displayNameContains in the query when provided", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { items: [], nextCursor: null, prevCursor: null }, error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(
      () => useServicesList({ sortBy: "displayName", sortOrder: "asc", displayNameContains: "pay" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(get).toHaveBeenCalledWith("/api/v1/catalog/services", expect.objectContaining({
      params: { query: expect.objectContaining({ displayNameContains: "pay" }) },
    }));
  });

  it("omits displayNameContains when not set", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { items: [], nextCursor: null, prevCursor: null }, error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(
      () => useServicesList({ sortBy: "displayName", sortOrder: "asc" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    const sentQuery = get.mock.calls[0]?.[1].params.query;
    expect(sentQuery).not.toHaveProperty("displayNameContains");
  });

  it("invalidates the services cache after a successful register", async () => {
    const post = vi.fn().mockResolvedValue({ data: { id: "svc-1" }, error: undefined, response: { status: 201 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, "invalidateQueries");
    const ui = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    );

    const { result } = renderHook(() => useRegisterService(), { wrapper: ui });
    await result.current.mutateAsync({ displayName: "Orders", description: "d", teamId: "tm", endpoints: [] });

    await waitFor(() => expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["services"] }));
  });
});
