import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useExplorerState } from "@/features/catalog/relationships/useExplorerState";

// Minimal in-memory Storage stand-in.
function memStorage(): Storage {
  const m = new Map<string, string>();
  return {
    get length() { return m.size; },
    clear: () => m.clear(),
    getItem: (k) => (m.has(k) ? m.get(k)! : null),
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => void m.delete(k),
    setItem: (k, v) => void m.set(k, v),
  };
}

describe("useExplorerState", () => {
  let store: Storage;
  beforeEach(() => { store = memStorage(); });

  it("starts empty and toggles a directional expand entry on/off", () => {
    const { result } = renderHook(() => useExplorerState("application:f", store));
    expect(result.current.expand).toEqual([]);
    act(() => result.current.toggleExpand("application:a", "out"));
    expect(result.current.isExpanded("application:a", "out")).toBe(true);
    expect(result.current.isExpanded("application:a", "in")).toBe(false);
    act(() => result.current.toggleExpand("application:a", "out"));
    expect(result.current.isExpanded("application:a", "out")).toBe(false);
  });

  it("persists to storage and restores on a fresh hook with the same key", () => {
    const { result, unmount } = renderHook(() => useExplorerState("application:f", store));
    act(() => result.current.toggleExpand("service:s", "in"));
    act(() => result.current.select("service:s"));
    unmount();
    const { result: r2 } = renderHook(() => useExplorerState("application:f", store));
    expect(r2.current.isExpanded("service:s", "in")).toBe(true);
    expect(r2.current.selected).toBe("service:s");
  });

  it("keeps independent state per focus key", () => {
    const { result, rerender } = renderHook(({ k }) => useExplorerState(k, store), {
      initialProps: { k: "application:f1" },
    });
    act(() => result.current.toggleExpand("application:a", "out"));
    rerender({ k: "application:f2" });
    expect(result.current.expand).toEqual([]); // f2 is fresh
    rerender({ k: "application:f1" });
    expect(result.current.isExpanded("application:a", "out")).toBe(true); // f1 restored
  });

  it("reset clears expand + selected", () => {
    const { result } = renderHook(() => useExplorerState("application:f", store));
    act(() => { result.current.toggleExpand("application:a", "out"); result.current.select("application:a"); });
    act(() => result.current.reset());
    expect(result.current.expand).toEqual([]);
    expect(result.current.selected).toBeNull();
  });

  it("survives corrupt JSON without throwing", () => {
    store.setItem("graph-explorer:application:f", "{not json");
    const { result } = renderHook(() => useExplorerState("application:f", store));
    expect(result.current.expand).toEqual([]);
    expect(result.current.selected).toBeNull();
  });
});
