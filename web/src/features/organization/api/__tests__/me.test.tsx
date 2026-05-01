import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import { orgKeys, useCurrentOrganization } from "../me";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe("useCurrentOrganization", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("derives stable query key", () => {
    expect(orgKeys.me).toEqual(["organization", "me"]);
  });

  it("fetches /api/v1/organizations/me and exposes data", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { id: "o1", name: "Acme Corp" },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => useCurrentOrganization(), { wrapper: makeWrapper(qc) });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(get).toHaveBeenCalledWith("/api/v1/organizations/me");
    expect(result.current.data).toEqual({ id: "o1", name: "Acme Corp" });
  });

  it("surfaces API errors as query error state", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "boom" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => useCurrentOrganization(), { wrapper: makeWrapper(qc) });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
