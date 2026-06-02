import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import { useUser, useUserSearch } from "../users";

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

function mockApiClient(impl: { GET?: ReturnType<typeof vi.fn> }) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: vi.fn(),
    PUT: vi.fn(),
    DELETE: vi.fn(),
  } as never);
}

const USER = {
  id: "u1",
  email: "alice@example.com",
  displayName: "Alice",
  givenName: "Alice",
  familyName: "Doe",
  teams: [],
  createdAt: "2026-01-01T00:00:00Z",
  lastSeenAt: null,
};

describe("useUser", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls GET /api/v1/organizations/users/{id} with the path param", async () => {
    const get = vi.fn().mockResolvedValue({ data: USER, error: undefined });
    mockApiClient({ GET: get });

    const { result } = renderHook(() => useUser("u1"), {
      wrapper: makeWrapper(newQc()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(get).toHaveBeenCalledWith(
      "/api/v1/organizations/users/{id}",
      expect.objectContaining({ params: { path: { id: "u1" } } }),
    );
    expect(result.current.data).toEqual(USER);
  });

  it("attaches __status when the server returns an error", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Not Found" },
      response: { status: 404 },
    });
    mockApiClient({ GET: get });

    const { result } = renderHook(() => useUser("u-missing"), {
      wrapper: makeWrapper(newQc()),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toMatchObject({ __status: 404 });
  });

  it.each([null, undefined, ""] as const)("does not fetch when id is %p", (id) => {
    const get = vi.fn();
    mockApiClient({ GET: get });

    renderHook(() => useUser(id), { wrapper: makeWrapper(newQc()) });

    expect(get).not.toHaveBeenCalled();
  });
});

describe("useUserSearch", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls GET /api/v1/organizations/users with q + limit and returns the array", async () => {
    const results = [
      { id: "u1", displayName: "Alice", email: "alice@example.com" },
      { id: "u2", displayName: "Alex", email: "alex@example.com" },
    ];
    const get = vi.fn().mockResolvedValue({ data: results, error: undefined });
    mockApiClient({ GET: get });

    const { result } = renderHook(() => useUserSearch("al", { limit: 5 }), {
      wrapper: makeWrapper(newQc()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(get).toHaveBeenCalledWith(
      "/api/v1/organizations/users",
      expect.objectContaining({ params: { query: { q: "al", limit: 5 } } }),
    );
    expect(result.current.data).toEqual(results);
  });

  it("does not fetch when enabled=false (caller-gated) even with a non-empty q", () => {
    const get = vi.fn();
    mockApiClient({ GET: get });

    renderHook(() => useUserSearch("al", { enabled: false }), {
      wrapper: makeWrapper(newQc()),
    });

    expect(get).not.toHaveBeenCalled();
  });

  it("does not fetch when q is empty and `enabled` is unset (defaults to q.length > 0)", () => {
    const get = vi.fn();
    mockApiClient({ GET: get });

    renderHook(() => useUserSearch(""), { wrapper: makeWrapper(newQc()) });

    expect(get).not.toHaveBeenCalled();
  });

  it("attaches __status when the server returns a 422", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Unprocessable" },
      response: { status: 422 },
    });
    mockApiClient({ GET: get });

    const { result } = renderHook(() => useUserSearch("a"), {
      wrapper: makeWrapper(newQc()),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toMatchObject({ __status: 422 });
  });
});
