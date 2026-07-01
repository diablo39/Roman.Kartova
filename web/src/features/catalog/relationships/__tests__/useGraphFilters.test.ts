import { describe, it, expect } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useGraphFilters } from "@/features/catalog/relationships/useGraphFilters";

function makeStorage(): Storage {
  const m = new Map<string, string>();
  return {
    get length() { return m.size; },
    clear: () => m.clear(),
    getItem: (k) => m.get(k) ?? null,
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => void m.delete(k),
    setItem: (k, v) => void m.set(k, v),
  };
}

describe("useGraphFilters", () => {
  it("defaults to empty filters", () => {
    const { result } = renderHook(() => useGraphFilters("application:focus", makeStorage()));
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
    expect(result.current.isActive).toBe(false);
    expect(result.current.activeCount).toBe(0);
  });

  it("persists and restores per focus key", () => {
    const storage = makeStorage();
    const first = renderHook(() => useGraphFilters("application:focus", storage));
    act(() => first.result.current.setKinds(["service"]));
    act(() => first.result.current.setTeamIds(["t1", "t2"]));
    expect(first.result.current.isActive).toBe(true);
    expect(first.result.current.activeCount).toBe(3);
    // a fresh hook on the same key reads the persisted value
    const second = renderHook(() => useGraphFilters("application:focus", storage));
    expect(second.result.current.filters).toEqual({ kinds: ["service"], teamIds: ["t1", "t2"] });
  });

  it("keeps independent state per focus key", () => {
    const storage = makeStorage();
    const a = renderHook(() => useGraphFilters("application:a", storage));
    act(() => a.result.current.setKinds(["application"]));
    const b = renderHook(() => useGraphFilters("service:b", storage));
    expect(b.result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });

  it("clear() resets to empty", () => {
    const storage = makeStorage();
    const { result } = renderHook(() => useGraphFilters("application:focus", storage));
    act(() => result.current.setTeamIds(["t1"]));
    act(() => result.current.clear());
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });

  it("falls back to empty on corrupt JSON without throwing", () => {
    const storage = makeStorage();
    storage.setItem("graph-explorer-filters:application:focus", "{not json");
    const { result } = renderHook(() => useGraphFilters("application:focus", storage));
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });

  it("reconciles to the new focus key's stored state when focusKey changes (render-time, not effect)", () => {
    const storage = makeStorage();
    storage.setItem(
      "graph-explorer-filters:service:b",
      JSON.stringify({ kinds: ["service"], teamIds: [] }),
    );
    const { result, rerender } = renderHook(({ key }) => useGraphFilters(key, storage), {
      initialProps: { key: "application:a" },
    });
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
    rerender({ key: "service:b" });
    // Immediately reflects the new key's persisted value — the render-time prev-key
    // reconcile, not a one-render-late effect.
    expect(result.current.filters).toEqual({ kinds: ["service"], teamIds: [] });
  });

  it("keeps in-memory state and never throws when storage.setItem throws (private mode / quota)", () => {
    const storage = makeStorage();
    storage.setItem = () => {
      throw new Error("QuotaExceededError");
    };
    const { result } = renderHook(() => useGraphFilters("application:focus", storage));
    expect(() => act(() => result.current.setKinds(["application"]))).not.toThrow();
    expect(result.current.filters.kinds).toEqual(["application"]);
  });
});
