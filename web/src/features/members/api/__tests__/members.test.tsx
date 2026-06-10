import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import { memberKeys, useMembersList, useChangeMemberRole, useOffboardMember } from "../members";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function newQc(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

function mockApiClient(impl: {
  GET?: ReturnType<typeof vi.fn>;
  PUT?: ReturnType<typeof vi.fn>;
  DELETE?: ReturnType<typeof vi.fn>;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: vi.fn(),
    PUT: impl.PUT ?? vi.fn(),
    DELETE: impl.DELETE ?? vi.fn(),
  } as never);
}

const MEMBER_PAGE = {
  items: [
    {
      id: "u1",
      displayName: "Alice",
      email: "alice@example.com",
      role: "OrgAdmin",
      teamCount: 2,
      lastSeenAt: null,
      createdAt: "2026-01-01T00:00:00Z",
    },
  ],
  nextCursor: null,
  prevCursor: null,
};

describe("useMembersList", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls GET /api/v1/organizations/users with sortBy + sortOrder and surfaces items", async () => {
    const get = vi.fn().mockResolvedValue({ data: MEMBER_PAGE, error: undefined });
    mockApiClient({ GET: get });

    const { result } = renderHook(
      () => useMembersList({ sortBy: "displayName", sortOrder: "asc" }),
      { wrapper: makeWrapper(newQc()) },
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(get).toHaveBeenCalledWith(
      "/api/v1/organizations/users",
      expect.objectContaining({
        params: {
          query: expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }),
        },
      }),
    );
    expect(result.current.items).toEqual(MEMBER_PAGE.items);
  });
});

describe("useChangeMemberRole", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls PUT /api/v1/organizations/users/{id}/role with path id and role body", async () => {
    const put = vi.fn().mockResolvedValue({ error: undefined, response: { status: 204 } });
    mockApiClient({ PUT: put });

    const { result } = renderHook(() => useChangeMemberRole(), {
      wrapper: makeWrapper(newQc()),
    });

    await act(async () => {
      await result.current.mutateAsync({ userId: "u1", role: "OrgAdmin" });
    });

    expect(put).toHaveBeenCalledWith(
      "/api/v1/organizations/users/{id}/role",
      { params: { path: { id: "u1" } }, body: { role: "OrgAdmin" } },
    );
  });

  it("attaches __status to the thrown error on a 409 response", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Conflict" },
      response: { status: 409 },
    });
    mockApiClient({ PUT: put });

    const qc = newQc();
    const { result } = renderHook(() => useChangeMemberRole(), {
      wrapper: makeWrapper(qc),
    });

    await expect(
      result.current.mutateAsync({ userId: "u1", role: "OrgAdmin" }),
    ).rejects.toMatchObject({ __status: 409 });
  });

  it("invalidates memberKeys.all on success", async () => {
    const put = vi.fn().mockResolvedValue({ error: undefined, response: { status: 204 } });
    mockApiClient({ PUT: put });

    const qc = newQc();
    const invalidate = vi.spyOn(qc, "invalidateQueries");

    const { result } = renderHook(() => useChangeMemberRole(), {
      wrapper: makeWrapper(qc),
    });

    await act(async () => {
      await result.current.mutateAsync({ userId: "u1", role: "OrgAdmin" });
    });

    expect(invalidate).toHaveBeenCalledWith({ queryKey: memberKeys.all });
  });
});

describe("useOffboardMember", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls DELETE /api/v1/organizations/users/{id} with path id only (no body — plain confirm)", async () => {
    const del = vi.fn().mockResolvedValue({ error: undefined, response: { status: 204 } });
    mockApiClient({ DELETE: del });

    const { result } = renderHook(() => useOffboardMember(), {
      wrapper: makeWrapper(newQc()),
    });

    await act(async () => {
      await result.current.mutateAsync({ userId: "u1" });
    });

    expect(del).toHaveBeenCalledWith(
      "/api/v1/organizations/users/{id}",
      { params: { path: { id: "u1" } } },
    );
  });

  it("attaches __status to the thrown error on a 409 response", async () => {
    const del = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Conflict" },
      response: { status: 409 },
    });
    mockApiClient({ DELETE: del });

    const qc = newQc();
    const { result } = renderHook(() => useOffboardMember(), {
      wrapper: makeWrapper(qc),
    });

    await expect(
      result.current.mutateAsync({ userId: "u1" }),
    ).rejects.toMatchObject({ __status: 409 });
  });

  it("invalidates memberKeys.all on success", async () => {
    const del = vi.fn().mockResolvedValue({ error: undefined, response: { status: 204 } });
    mockApiClient({ DELETE: del });

    const qc = newQc();
    const invalidate = vi.spyOn(qc, "invalidateQueries");

    const { result } = renderHook(() => useOffboardMember(), {
      wrapper: makeWrapper(qc),
    });

    await act(async () => {
      await result.current.mutateAsync({ userId: "u1" });
    });

    expect(invalidate).toHaveBeenCalledWith({ queryKey: memberKeys.all });
  });
});
