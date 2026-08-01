import { it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import * as clientModule from "@/features/catalog/api/client";
import * as rel from "@/features/catalog/api/relationships";
import { useComponentSystem, useSetComponentSystem } from "@/features/catalog/api/systems";

function listResult(items: Partial<rel.RelationshipResponse>[]) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() } as never;
}

function wrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}
const newQc = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

beforeEach(() => vi.restoreAllMocks());

it("derives the current System from the outgoing partOf edge", () => {
  const spy = vi.spyOn(rel, "useRelationshipsList").mockReturnValue(listResult([
    { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "application", id: "a1", displayName: "Me" }, target: { kind: "service", id: "s9", displayName: "Other" } },
    { id: "r2", type: "partOf", origin: "manual", source: { kind: "application", id: "a1", displayName: "Me" }, target: { kind: "system", id: "sys1", displayName: "Payments" } },
  ]));

  const { result } = renderHook(() => useComponentSystem("application", "a1"));

  expect(result.current.systemId).toBe("sys1");
  expect(result.current.systemDisplayName).toBe("Payments");
  // Assert the REQUEST shape, not just the derived output: deleting `type: "partOf"` (or
  // widening `limit`) from systems.ts would still pass the assertions above against this test
  // double, since the double ignores its arguments — only pinning the call args catches it.
  expect(spy).toHaveBeenCalledWith(
    expect.objectContaining({ entityKind: "application", entityId: "a1", direction: "outgoing", type: "partOf", limit: 1 }),
  );
});

it("reports no membership when there is no partOf edge", () => {
  const spy = vi.spyOn(rel, "useRelationshipsList").mockReturnValue(listResult([
    { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "Other" } },
  ]));

  const { result } = renderHook(() => useComponentSystem("service", "s1"));

  expect(result.current.systemId).toBeNull();
  expect(result.current.systemDisplayName).toBeNull();
  expect(spy).toHaveBeenCalledWith(
    expect.objectContaining({ entityKind: "service", entityId: "s1", direction: "outgoing", type: "partOf", limit: 1 }),
  );
});

it("useSetComponentSystem PUTs the application-system path for componentKind application", async () => {
  const put = vi.fn().mockResolvedValue({
    data: { systemId: "sys1", systemDisplayName: "Payments" },
    error: undefined,
    response: new Response(),
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ PUT: put } as never);
  const qc = newQc();

  const { result } = renderHook(() => useSetComponentSystem(), { wrapper: wrapper(qc) });
  await result.current.mutateAsync({ componentKind: "application", componentId: "a1", systemId: "sys1" });

  // Assert the actual URL string and path id — a branch inversion would still "PUT something"
  // but would hit the wrong resource, which only a URL-string assertion catches.
  expect(put).toHaveBeenCalledWith("/api/v1/catalog/applications/{id}/system", {
    params: { path: { id: "a1" } },
    body: { systemId: "sys1" },
  });
});

it("useSetComponentSystem PUTs the service-system path for componentKind service", async () => {
  const put = vi.fn().mockResolvedValue({
    data: { systemId: null, systemDisplayName: null },
    error: undefined,
    response: new Response(),
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ PUT: put } as never);
  const qc = newQc();

  const { result } = renderHook(() => useSetComponentSystem(), { wrapper: wrapper(qc) });
  await result.current.mutateAsync({ componentKind: "service", componentId: "s1", systemId: null });

  expect(put).toHaveBeenCalledWith("/api/v1/catalog/services/{id}/system", {
    params: { path: { id: "s1" } },
    body: { systemId: null },
  });
});

it("useSetComponentSystem invalidates relationships, catalog, and systems on success", async () => {
  const put = vi.fn().mockResolvedValue({
    data: { systemId: "sys1", systemDisplayName: "Payments" },
    error: undefined,
    response: new Response(),
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ PUT: put } as never);
  const qc = newQc();
  const invalidate = vi.spyOn(qc, "invalidateQueries");

  const { result } = renderHook(() => useSetComponentSystem(), { wrapper: wrapper(qc) });
  await result.current.mutateAsync({ componentKind: "application", componentId: "a1", systemId: "sys1" });

  await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ["relationships"] }));
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ["catalog"] });
  expect(invalidate).toHaveBeenCalledWith({ queryKey: ["systems"] });
});

it("useSetComponentSystem attaches the response status to the thrown error on failure", async () => {
  const problem = { title: "Conflict", status: 409 };
  const put = vi.fn().mockResolvedValue({
    data: undefined,
    error: problem,
    response: new Response(null, { status: 409 }),
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ PUT: put } as never);
  const qc = newQc();

  const { result } = renderHook(() => useSetComponentSystem(), { wrapper: wrapper(qc) });

  await expect(
    result.current.mutateAsync({ componentKind: "application", componentId: "a1", systemId: "sys2" }),
  ).rejects.toMatchObject({ title: "Conflict", status: 409, __status: 409 });
});
