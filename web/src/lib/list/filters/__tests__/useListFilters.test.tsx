import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import { useListFilters } from "../useListFilters";
import type { FilterSpec } from "../types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fakeTextState(committed: Record<string, string> = {}) {
  return {
    textFilters: committed as Record<string, string>,
    booleanFilters: {} as Record<string, boolean>,
  };
}

function fakeMixedState(text: Record<string, string> = {}, bool: Record<string, boolean> = {}) {
  return {
    textFilters: text as Record<string, string>,
    booleanFilters: bool as Record<string, boolean>,
  };
}

// ---------------------------------------------------------------------------
// Text specs only
// ---------------------------------------------------------------------------

const textSpecs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

describe("useListFilters — text specs", () => {
  it("queryFilters returns the committed value (non-empty → string)", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "pl" })),
    );
    expect(result.current.queryFilters.displayNameContains).toBe("pl");
  });

  it("queryFilters returns undefined for an empty committed value", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "" })),
    );
    expect(result.current.queryFilters.displayNameContains).toBeUndefined();
  });

  it("queryFilters returns undefined when the key is absent from textFilters", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({})),
    );
    expect(result.current.queryFilters.displayNameContains).toBeUndefined();
  });

  it("isActive is true when at least one text filter has a committed non-empty value", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "pl" })),
    );
    expect(result.current.isActive).toBe(true);
  });

  it("isActive is false when committed text is empty", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "" })),
    );
    expect(result.current.isActive).toBe(false);
  });

  it("activeCount reflects the number of active text filters", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "pl" })),
    );
    expect(result.current.activeCount).toBe(1);
  });

  it("activeCount is 0 when committed text is empty", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "" })),
    );
    expect(result.current.activeCount).toBe(0);
  });

  it("returns only { queryFilters, isActive, activeCount } — no draft/bind/submit/clearAll", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState()),
    );
    const keys = Object.keys(result.current);
    expect(keys).toEqual(expect.arrayContaining(["queryFilters", "isActive", "activeCount"]));
    expect(keys).not.toContain("values");
    expect(keys).not.toContain("bind");
    expect(keys).not.toContain("submit");
    expect(keys).not.toContain("clearAll");
  });
});

// ---------------------------------------------------------------------------
// Boolean specs
// ---------------------------------------------------------------------------

const boolSpecs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search", placeholder: "…" },
  { key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" },
];

describe("useListFilters — boolean specs", () => {
  it("queryFilters.includeDecommissioned defaults to false when absent from booleanFilters", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, {})),
    );
    expect(result.current.queryFilters.includeDecommissioned).toBe(false);
  });

  it("queryFilters.includeDecommissioned is true when committed to true", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true })),
    );
    expect(result.current.queryFilters.includeDecommissioned).toBe(true);
  });

  it("queryFilters.includeDecommissioned is false when committed to false", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false })),
    );
    expect(result.current.queryFilters.includeDecommissioned).toBe(false);
  });

  it("isActive is true when a committed boolean is true", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true })),
    );
    expect(result.current.isActive).toBe(true);
  });

  it("isActive is false when boolean is false and text is empty", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false })),
    );
    expect(result.current.isActive).toBe(false);
  });

  it("activeCount counts a committed true boolean", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true })),
    );
    expect(result.current.activeCount).toBe(1);
  });

  it("activeCount counts both a non-empty text and a true boolean", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "foo" }, { includeDecommissioned: true })),
    );
    expect(result.current.activeCount).toBe(2);
  });
});

// ---------------------------------------------------------------------------
// Mixed: re-renders with new committed state (simulates back/forward nav)
// ---------------------------------------------------------------------------

describe("useListFilters — re-renders with changed committed state", () => {
  it("queryFilters updates when committed textFilters changes (e.g. back-nav)", () => {
    let urlState = fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false });
    const { result, rerender } = renderHook(() => useListFilters(boolSpecs, urlState));

    expect(result.current.queryFilters.displayNameContains).toBeUndefined();

    urlState = fakeMixedState({ displayNameContains: "plat" }, { includeDecommissioned: false });
    rerender();

    expect(result.current.queryFilters.displayNameContains).toBe("plat");
    expect(result.current.isActive).toBe(true);
  });

  it("queryFilters updates when committed booleanFilters changes from outside", () => {
    let urlState = fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false });
    const { result, rerender } = renderHook(() => useListFilters(boolSpecs, urlState));

    expect(result.current.queryFilters.includeDecommissioned).toBe(false);

    urlState = fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true });
    rerender();

    expect(result.current.queryFilters.includeDecommissioned).toBe(true);
    expect(result.current.isActive).toBe(true);
  });
});
