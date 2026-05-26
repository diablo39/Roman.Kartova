import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import {
  teamKeys,
  useTeam,
  useTeamsList,
  useCreateTeam,
  useDeleteTeam,
} from "../teams";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
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

const DEFAULT_PARAMS = { sortBy: "createdAt" as const, sortOrder: "desc" as const };

describe("teams hooks", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("useTeamsList", () => {
    it("calls GET /api/v1/organizations/teams with sort params and exposes items", async () => {
      const page = {
        items: [{ id: "t1", displayName: "Platform", description: "", createdAt: "2026-01-01T00:00:00Z" }],
        nextCursor: null,
        prevCursor: null,
      };
      const get = vi.fn().mockResolvedValue({ data: page, error: undefined });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useTeamsList(DEFAULT_PARAMS), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/teams",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({ sortBy: "createdAt", sortOrder: "desc" }),
          }),
        }),
      );
      expect(result.current.items).toHaveLength(1);
    });
  });

  describe("useTeam", () => {
    it("calls GET /api/v1/organizations/teams/{id} with the path param", async () => {
      const get = vi.fn().mockResolvedValue({
        data: { id: "t1", displayName: "Platform", description: "", createdAt: "2026-01-01T00:00:00Z", members: [], applications: [] },
        error: undefined,
      });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useTeam("t1"), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/teams/{id}",
        { params: { path: { id: "t1" } } },
      );
    });

    it("does not fetch when id is empty", () => {
      const get = vi.fn();
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      renderHook(() => useTeam(""), { wrapper: makeWrapper(qc) });

      expect(get).not.toHaveBeenCalled();
    });
  });

  describe("useCreateTeam", () => {
    it("posts the body and invalidates teamKeys.all on success", async () => {
      const post = vi.fn().mockResolvedValue({
        data: { id: "t2", displayName: "Ops", description: "", createdAt: "2026-01-01T00:00:00Z" },
        error: undefined,
        response: { status: 201 },
      });
      mockApiClient({ POST: post });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useCreateTeam(), { wrapper: makeWrapper(qc) });
      await result.current.mutateAsync({ displayName: "Ops", description: "" });

      expect(post).toHaveBeenCalledWith(
        "/api/v1/organizations/teams",
        { body: { displayName: "Ops", description: "" } },
      );
      expect(invalidate).toHaveBeenCalledWith({ queryKey: teamKeys.all });
    });
  });

  describe("useDeleteTeam", () => {
    it("calls DELETE and on 409 attaches __status to the thrown error", async () => {
      const del = vi.fn().mockResolvedValue({
        data: undefined,
        error: { title: "Conflict", applicationCount: 3 },
        response: { status: 409 },
      });
      mockApiClient({ DELETE: del });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useDeleteTeam("t9"), { wrapper: makeWrapper(qc) });

      await expect(result.current.mutateAsync()).rejects.toMatchObject({
        __status: 409,
        applicationCount: 3,
      });
      expect(del).toHaveBeenCalledWith(
        "/api/v1/organizations/teams/{id}",
        { params: { path: { id: "t9" } } },
      );
    });
  });
});
