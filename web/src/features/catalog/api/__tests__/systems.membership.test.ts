import { it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import * as rel from "@/features/catalog/api/relationships";
import { useComponentSystem } from "@/features/catalog/api/systems";

function listResult(items: Partial<rel.RelationshipResponse>[]) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() } as never;
}

beforeEach(() => vi.restoreAllMocks());

it("derives the current System from the outgoing partOf edge", () => {
  vi.spyOn(rel, "useRelationshipsList").mockReturnValue(listResult([
    { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "application", id: "a1", displayName: "Me" }, target: { kind: "service", id: "s9", displayName: "Other" } },
    { id: "r2", type: "partOf", origin: "manual", source: { kind: "application", id: "a1", displayName: "Me" }, target: { kind: "system", id: "sys1", displayName: "Payments" } },
  ]));

  const { result } = renderHook(() => useComponentSystem("application", "a1"));

  expect(result.current.systemId).toBe("sys1");
  expect(result.current.systemDisplayName).toBe("Payments");
});

it("reports no membership when there is no partOf edge", () => {
  vi.spyOn(rel, "useRelationshipsList").mockReturnValue(listResult([
    { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "Other" } },
  ]));

  const { result } = renderHook(() => useComponentSystem("service", "s1"));

  expect(result.current.systemId).toBeNull();
  expect(result.current.systemDisplayName).toBeNull();
});
