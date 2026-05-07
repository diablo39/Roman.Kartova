import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "../client";
import { applicationKeys, useDecommissionApplication } from "../applications";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function mockApiClient(impl: {
  GET?: ReturnType<typeof vi.fn>;
  POST?: ReturnType<typeof vi.fn>;
  PUT?: ReturnType<typeof vi.fn>;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: impl.POST ?? vi.fn(),
    PUT: impl.PUT ?? vi.fn(),
  } as never);
}

const decommissionedResponse = {
  id: "abc",
  tenantId: "t",
  name: "x",
  displayName: "X",
  description: "Y",
  ownerUserId: "u",
  createdAt: "2026-01-01T00:00:00Z",
  lifecycle: "decommissioned",
  sunsetDate: "2026-04-01T00:00:00Z",
  version: "v4",
};

describe("useDecommissionApplication", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("POSTs to /decommission with no body and warms caches on success", async () => {
    const post = vi.fn().mockResolvedValue({
      data: decommissionedResponse,
      error: undefined,
      response: { status: 200 } as Response,
    });
    mockApiClient({ POST: post });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const setQueryData = vi.spyOn(qc, "setQueryData");
    const invalidate = vi.spyOn(qc, "invalidateQueries");

    const { result } = renderHook(() => useDecommissionApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    const data = await result.current.mutateAsync();

    expect(data).toEqual(decommissionedResponse);
    expect(post).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/decommission",
      { params: { path: { id: "abc" } } }
    );
    expect(setQueryData).toHaveBeenCalledWith(applicationKeys.detail("abc"), decommissionedResponse);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.list() });
  });

  it("attaches __status on 409 before-sunset-date so the dialog can branch", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/lifecycle-conflict",
        detail: "before-sunset-date",
      },
      response: { status: 409 } as Response,
    });
    mockApiClient({ POST: post });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => useDecommissionApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(async () => {
      await expect(result.current.mutateAsync()).rejects.toMatchObject({ __status: 409 });
    });
  });
});
