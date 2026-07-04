import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";

const getMock = vi.fn();
vi.mock("../client", () => ({ apiClient: { GET: (...a: unknown[]) => getMock(...a) } }));

import { useApisList } from "../apis";

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  getMock.mockReset();
  getMock.mockResolvedValue({ data: { items: [], nextCursor: null, prevCursor: null }, error: undefined });
});

describe("useApisList", () => {
  it("passes style/teamId/displayNameContains only when non-empty", async () => {
    renderHook(() => useApisList({
      sortBy: "displayName", sortOrder: "asc",
      style: ["rest"], teamId: ["t1"], displayNameContains: "ord",
    }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0]?.[1].params.query;
    expect(query.style).toEqual(["rest"]);
    expect(query.teamId).toEqual(["t1"]);
    expect(query.displayNameContains).toBe("ord");
  });

  it("omits empty filter arrays", async () => {
    renderHook(() => useApisList({ sortBy: "displayName", sortOrder: "asc", style: [], teamId: [] }), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalled());
    const query = getMock.mock.calls[0]?.[1].params.query;
    expect(query.style).toBeUndefined();
    expect(query.teamId).toBeUndefined();
    expect(query.displayNameContains).toBeUndefined();
  });
});
