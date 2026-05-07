import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "../client";
import { applicationKeys, useDeprecateApplication } from "../applications";

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

const deprecatedResponse = {
  id: "abc",
  tenantId: "t",
  name: "x",
  displayName: "X",
  description: "Y",
  ownerUserId: "u",
  createdAt: "2026-01-01T00:00:00Z",
  lifecycle: "deprecated",
  sunsetDate: "2026-12-01T00:00:00Z",
  version: "v3",
};

describe("useDeprecateApplication", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("POSTs to /deprecate with the sunsetDate body and warms caches on success", async () => {
    const post = vi.fn().mockResolvedValue({
      data: deprecatedResponse,
      error: undefined,
      response: { status: 200 } as Response,
    });
    mockApiClient({ POST: post });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const setQueryData = vi.spyOn(qc, "setQueryData");
    const invalidate = vi.spyOn(qc, "invalidateQueries");

    const { result } = renderHook(() => useDeprecateApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    const data = await result.current.mutateAsync({ sunsetDate: "2026-12-01T00:00:00Z" });

    expect(data).toEqual(deprecatedResponse);
    expect(post).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/deprecate",
      {
        params: { path: { id: "abc" } },
        body: { sunsetDate: "2026-12-01T00:00:00Z" },
      }
    );
    expect(setQueryData).toHaveBeenCalledWith(applicationKeys.detail("abc"), deprecatedResponse);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.list() });
  });

  it("attaches __status on 409 LifecycleConflict so the dialog can branch", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/lifecycle-conflict" },
      response: { status: 409 } as Response,
    });
    mockApiClient({ POST: post });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { result } = renderHook(() => useDeprecateApplication("abc"), {
      wrapper: makeWrapper(qc),
    });

    await waitFor(async () => {
      await expect(
        result.current.mutateAsync({ sunsetDate: "2026-12-01T00:00:00Z" })
      ).rejects.toMatchObject({ __status: 409 });
    });
  });
});
