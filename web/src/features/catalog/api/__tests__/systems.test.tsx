import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import * as clientModule from "@/features/catalog/api/client";
import { useSystem, useSystemsList, useRegisterSystem } from "../systems";

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

const sys = { id: "s1", tenantId: "t1", displayName: "Alpha", description: null, teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null };
const page = { items: [sys], nextCursor: null, prevCursor: null };

describe("api/systems", () => {
  beforeEach(() => vi.restoreAllMocks());

  function stubGet(returnValue: unknown = page) {
    const get = vi.fn().mockResolvedValue({ data: returnValue, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    return get;
  }

  it("omits teamId/displayNameContains from the query when empty", async () => {
    const get = stubGet();
    const { result } = renderHook(() => useSystemsList({ sortBy: "displayName", sortOrder: "asc" }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.items.length).toBe(1));
    const query = get.mock.calls[0][1].params.query;
    expect(query.sortBy).toBe("displayName");
    expect(query).not.toHaveProperty("teamId");
    expect(query).not.toHaveProperty("displayNameContains");
  });

  it("passes teamId (array) + displayNameContains when set", async () => {
    const get = stubGet();
    const { result } = renderHook(
      () => useSystemsList({ sortBy: "displayName", sortOrder: "asc", teamId: ["a", "b"], displayNameContains: "pay" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.items.length).toBe(1));
    const query = get.mock.calls[0][1].params.query;
    expect(query.teamId).toEqual(["a", "b"]);
    expect(query.displayNameContains).toBe("pay");
  });

  it("fetches a single system by id", async () => {
    stubGet(sys);
    const { result } = renderHook(() => useSystem("s1"), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.data?.displayName).toBe("Alpha"));
  });

  it("POSTs the payload on register and invalidates the systems cache", async () => {
    const post = vi.fn().mockResolvedValue({ data: sys, error: undefined, response: new Response() });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(qc, "invalidateQueries");
    const w = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
    const { result } = renderHook(() => useRegisterSystem(), { wrapper: w });
    await result.current.mutateAsync({ displayName: "Alpha", teamId: "team1", description: "core" });
    expect(post.mock.calls[0][1].body).toMatchObject({ displayName: "Alpha", teamId: "team1", description: "core" });
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ["systems"] }));
  });

  it("omits a blank description from the POST body (sends undefined)", async () => {
    const post = vi.fn().mockResolvedValue({ data: sys, error: undefined, response: new Response() });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);
    const { result } = renderHook(() => useRegisterSystem(), { wrapper: wrapper() });
    await result.current.mutateAsync({ displayName: "Alpha", teamId: "team1", description: "   " });
    expect(post.mock.calls[0][1].body.description).toBeUndefined();
  });
});
