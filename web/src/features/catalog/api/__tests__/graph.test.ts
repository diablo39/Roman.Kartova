import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import * as clientModule from "@/features/catalog/api/client";
import { useGraph } from "@/features/catalog/api/graph";

// NOTE: this file is .ts (not .tsx, per the sibling relationships.test.tsx pattern) — the
// QueryClientProvider wrapper is built with React.createElement rather than JSX because JSX
// syntax is not valid in a .ts file.
function wrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: qc }, children);
}
const newQc = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

describe("useGraph", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("requests the depth it is given, not the fixed focus depth (FU-A)", async () => {
    const getSpy = vi.fn().mockResolvedValue({ data: { nodes: [], edges: [], truncated: false }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: getSpy } as never);
    const qc = newQc();

    renderHook(() => useGraph({ focus: { kind: "system", id: "s1" }, expand: [], depth: 1 }), { wrapper: wrapper(qc) });

    await waitFor(() => expect(getSpy).toHaveBeenCalled());
    expect(getSpy).toHaveBeenCalledWith("/api/v1/catalog/graph", {
      params: { query: { entityKind: "system", entityId: "s1", depth: 1, direction: "all" } },
    });
  });

  it("defaults to depth 2 when none is given", async () => {
    const getSpy = vi.fn().mockResolvedValue({ data: { nodes: [], edges: [], truncated: false }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: getSpy } as never);
    const qc = newQc();

    renderHook(() => useGraph({ focus: { kind: "service", id: "x" }, expand: [] }), { wrapper: wrapper(qc) });

    await waitFor(() => expect(getSpy).toHaveBeenCalled());
    expect(getSpy.mock.calls[0]![1].params.query.depth).toBe(2);
  });
});
