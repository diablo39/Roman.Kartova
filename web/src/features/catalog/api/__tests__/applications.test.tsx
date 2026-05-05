import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "../client";
import {
  applicationKeys,
  useApplication,
  useApplicationsList,
  useRegisterApplication,
} from "../applications";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function mockApiClient(impl: { GET?: ReturnType<typeof vi.fn>; POST?: ReturnType<typeof vi.fn> }) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: impl.POST ?? vi.fn(),
  } as never);
}

const DEFAULT_PARAMS = { sortBy: "createdAt" as const, sortOrder: "desc" as const };

describe("catalog hooks", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("applicationKeys", () => {
    it("derives stable list and detail keys (no params)", () => {
      expect(applicationKeys.list()).toEqual(["applications", "list"]);
      expect(applicationKeys.detail("abc")).toEqual(["applications", "detail", "abc"]);
    });

    it("derives parameterized list key", () => {
      expect(applicationKeys.list(DEFAULT_PARAMS)).toEqual([
        "applications",
        "list",
        DEFAULT_PARAMS,
      ]);
    });
  });

  describe("useApplicationsList", () => {
    it("calls GET /api/v1/catalog/applications with query params and exposes items", async () => {
      const page = { items: [{ id: "a1", name: "x", displayName: "X", tenantId: "t", description: "", ownerUserId: "u", createdAt: "2026-01-01T00:00:00Z" }], nextCursor: null, prevCursor: null };
      const get = vi.fn().mockResolvedValue({ data: page, error: undefined });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useApplicationsList(DEFAULT_PARAMS), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({ sortBy: "createdAt", sortOrder: "desc" }),
          }),
        })
      );
      expect(result.current.items).toHaveLength(1);
    });

    it("surfaces error when API returns error", async () => {
      const get = vi.fn().mockResolvedValue({ data: undefined, error: { status: 500, title: "boom" } });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useApplicationsList(DEFAULT_PARAMS), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  describe("useApplication", () => {
    it("calls GET /api/v1/catalog/applications/{id} with the path param", async () => {
      const get = vi.fn().mockResolvedValue({
        data: { id: "a1", name: "x", displayName: "X" },
        error: undefined,
      });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useApplication("a1"), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications/{id}",
        { params: { path: { id: "a1" } } }
      );
    });

    it("does not fetch when id is empty", () => {
      const get = vi.fn();
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      renderHook(() => useApplication(""), { wrapper: makeWrapper(qc) });

      expect(get).not.toHaveBeenCalled();
    });
  });

  describe("useRegisterApplication", () => {
    it("posts the body and invalidates the all applications prefix on success", async () => {
      const post = vi.fn().mockResolvedValue({
        data: { id: "a2", name: "n", displayName: "N" },
        error: undefined,
      });
      mockApiClient({ POST: post });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useRegisterApplication(), {
        wrapper: makeWrapper(qc),
      });

      await result.current.mutateAsync({ name: "n", displayName: "N", description: "" });

      expect(post).toHaveBeenCalledWith(
        "/api/v1/catalog/applications",
        { body: { name: "n", displayName: "N", description: "" } }
      );
      // Invalidation uses applicationKeys.all (prefix), covering all parameterized list keys.
      expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.all });
    });

    it("rejects and surfaces the api error to the mutation state", async () => {
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: { status: 400, errors: { name: ["bad"] } },
      });
      mockApiClient({ POST: post });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useRegisterApplication(), {
        wrapper: makeWrapper(qc),
      });

      await expect(
        result.current.mutateAsync({ name: "n", displayName: "N", description: "" })
      ).rejects.toMatchObject({ status: 400 });
    });
  });
});
