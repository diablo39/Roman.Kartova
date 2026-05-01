import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "../client";
import {
  applicationKeys,
  useApplication,
  useApplications,
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

describe("catalog hooks", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("applicationKeys", () => {
    it("derives stable list and detail keys", () => {
      expect(applicationKeys.list()).toEqual(["applications", "list"]);
      expect(applicationKeys.detail("abc")).toEqual(["applications", "detail", "abc"]);
    });
  });

  describe("useApplications", () => {
    it("calls GET /api/v1/catalog/applications and exposes data", async () => {
      const get = vi.fn().mockResolvedValue({
        data: [{ id: "a1", name: "x", displayName: "X" }],
        error: undefined,
      });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useApplications(), { wrapper: makeWrapper(qc) });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(get).toHaveBeenCalledWith("/api/v1/catalog/applications");
      expect(result.current.data).toHaveLength(1);
    });

    it("throws to query state when api returns error", async () => {
      const get = vi.fn().mockResolvedValue({
        data: undefined,
        error: { status: 500, title: "boom" },
      });
      mockApiClient({ GET: get });

      const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useApplications(), { wrapper: makeWrapper(qc) });

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
    it("posts the body and invalidates the list query on success", async () => {
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
      expect(invalidate).toHaveBeenCalledWith({ queryKey: applicationKeys.list() });
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
