import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as clientModule from "@/features/catalog/api/client";
import {
  useRelationshipsList, useCreateRelationship, useDeleteRelationship, useEntitySearch,
} from "@/features/catalog/api/relationships";

function wrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}
const newQc = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

describe("relationships api", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("useRelationshipsList fetches a directional page", async () => {
    const page = { items: [{ id: "r1", type: "DependsOn" }], nextCursor: null, prevCursor: null };
    const GET = vi.fn().mockResolvedValue({ data: page, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET } as never);

    const qc = newQc();
    const { result } = renderHook(
      () => useRelationshipsList({ entityKind: "Service", entityId: "s1", direction: "outgoing" }),
      { wrapper: wrapper(qc) },
    );
    await waitFor(() => expect(result.current.items).toHaveLength(1));
    expect(GET).toHaveBeenCalledWith("/api/v1/catalog/relationships", expect.objectContaining({
      params: { query: expect.objectContaining({ entityKind: "Service", entityId: "s1", direction: "outgoing" }) },
    }));
  });

  it("useCreateRelationship POSTs and invalidates", async () => {
    const POST = vi.fn().mockResolvedValue({ data: { id: "r1" }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ POST } as never);
    const qc = newQc();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const { result } = renderHook(() => useCreateRelationship(), { wrapper: wrapper(qc) });
    await result.current.mutateAsync({ sourceKind: "Service", sourceId: "s1", type: "DependsOn", targetKind: "Service", targetId: "s2" });
    expect(POST).toHaveBeenCalledWith("/api/v1/catalog/relationships", { body: expect.objectContaining({ type: "DependsOn" }) });
    expect(spy).toHaveBeenCalledWith({ queryKey: ["relationships"] });
  });

  it("useDeleteRelationship DELETEs by id and invalidates", async () => {
    const DELETE = vi.fn().mockResolvedValue({ data: undefined, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ DELETE } as never);
    const qc = newQc();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const { result } = renderHook(() => useDeleteRelationship(), { wrapper: wrapper(qc) });
    await result.current.mutateAsync("r1");
    expect(DELETE).toHaveBeenCalledWith("/api/v1/catalog/relationships/{id}", { params: { path: { id: "r1" } } });
    expect(spy).toHaveBeenCalledWith({ queryKey: ["relationships"] });
  });

  it("useEntitySearch hits the services endpoint for Service kind", async () => {
    const page = { items: [{ id: "s9", displayName: "AuthService" }], nextCursor: null, prevCursor: null };
    const GET = vi.fn().mockResolvedValue({ data: page, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET } as never);
    const qc = newQc();
    const { result } = renderHook(() => useEntitySearch("Service", "au", { enabled: true }), { wrapper: wrapper(qc) });
    await waitFor(() => expect(result.current.data).toEqual([{ kind: "Service", id: "s9", displayName: "AuthService" }]));
    expect(GET).toHaveBeenCalledWith("/api/v1/catalog/services", expect.objectContaining({
      params: { query: expect.objectContaining({ displayNameContains: "au", limit: 10 }) },
    }));
  });
});
