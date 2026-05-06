import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "../client";
import { applicationKeys, useEditApplication } from "../applications";

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

const successResponse = {
  id: "abc",
  tenantId: "t",
  name: "x",
  displayName: "X",
  description: "Y",
  ownerUserId: "u",
  createdAt: "2026-01-01T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
  version: "v2",
};

describe("useEditApplication", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("PUTs with If-Match header derived from expectedVersion and warms detail cache on success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: successResponse,
      error: undefined,
      response: { status: 200 } as Response,
    });
    mockApiClient({ PUT: put });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const setQueryData = vi.spyOn(qc, "setQueryData");
    const invalidate = vi.spyOn(qc, "invalidateQueries");

    const { result } = renderHook(() => useEditApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    const data = await result.current.mutateAsync({
      values: { displayName: "X", description: "Y" },
      expectedVersion: "v1",
    });

    expect(data).toEqual(successResponse);
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}",
      expect.objectContaining({
        params: { path: { id: "abc" } },
        body: { displayName: "X", description: "Y" },
        headers: { "If-Match": '"v1"' },
      })
    );
    expect(setQueryData).toHaveBeenCalledWith(applicationKeys.detail("abc"), successResponse);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.all });
  });

  it("attaches __status on error so the dialog can branch on 412 / 409 / 400", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict", title: "stale" },
      response: { status: 412 } as Response,
    });
    mockApiClient({ PUT: put });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => useEditApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(async () => {
      await expect(
        result.current.mutateAsync({
          values: { displayName: "X", description: "Y" },
          expectedVersion: "v1",
        })
      ).rejects.toMatchObject({ __status: 412 });
    });
  });
});
