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
    multiFilters: {} as Record<string, string[]>,
  };
}

function fakeMixedState(text: Record<string, string> = {}, bool: Record<string, boolean> = {}) {
  return {
    textFilters: text as Record<string, string>,
    booleanFilters: bool as Record<string, boolean>,
    multiFilters: {} as Record<string, string[]>,
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
    expect(result.current.textValues.displayNameContains).toBe("pl");
  });

  it("queryFilters returns undefined for an empty committed value", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({ displayNameContains: "" })),
    );
    expect(result.current.textValues.displayNameContains).toBeUndefined();
  });

  it("queryFilters returns undefined when the key is absent from textFilters", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState({})),
    );
    expect(result.current.textValues.displayNameContains).toBeUndefined();
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

  it("returns typed value maps + isActive/activeCount — no draft/bind/submit/clearAll", () => {
    const { result } = renderHook(() =>
      useListFilters(textSpecs, fakeTextState()),
    );
    const keys = Object.keys(result.current);
    expect(keys).toEqual(
      expect.arrayContaining(["textValues", "boolValues", "multiValues", "isActive", "activeCount"]),
    );
    expect(keys).not.toContain("queryFilters"); // replaced by the three typed maps
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
    expect(result.current.boolValues.includeDecommissioned).toBe(false);
  });

  it("queryFilters.includeDecommissioned is true when committed to true", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true })),
    );
    expect(result.current.boolValues.includeDecommissioned).toBe(true);
  });

  it("queryFilters.includeDecommissioned is false when committed to false", () => {
    const { result } = renderHook(() =>
      useListFilters(boolSpecs, fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false })),
    );
    expect(result.current.boolValues.includeDecommissioned).toBe(false);
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

    expect(result.current.textValues.displayNameContains).toBeUndefined();

    urlState = fakeMixedState({ displayNameContains: "plat" }, { includeDecommissioned: false });
    rerender();

    expect(result.current.textValues.displayNameContains).toBe("plat");
    expect(result.current.isActive).toBe(true);
  });

  it("queryFilters updates when committed booleanFilters changes from outside", () => {
    let urlState = fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: false });
    const { result, rerender } = renderHook(() => useListFilters(boolSpecs, urlState));

    expect(result.current.boolValues.includeDecommissioned).toBe(false);

    urlState = fakeMixedState({ displayNameContains: "" }, { includeDecommissioned: true });
    rerender();

    expect(result.current.boolValues.includeDecommissioned).toBe(true);
    expect(result.current.isActive).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Single-select specs
// ---------------------------------------------------------------------------

const selectSpecs: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
    ],
  },
];

describe("useListFilters — single-select", () => {
  it("exposes a committed single-select value as a queryFilter string", () => {
    const { result } = renderHook(() =>
      useListFilters(selectSpecs, { textFilters: { role: "Viewer" }, booleanFilters: {}, multiFilters: {} }),
    );
    expect(result.current.textValues.role).toBe("Viewer");
    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
  });

  it("treats a blank single-select as undefined / inactive", () => {
    const { result } = renderHook(() =>
      useListFilters(selectSpecs, { textFilters: { role: "" }, booleanFilters: {}, multiFilters: {} }),
    );
    expect(result.current.textValues.role).toBeUndefined();
    expect(result.current.isActive).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Multi-select specs
// ---------------------------------------------------------------------------

describe("useListFilters — multi-select", () => {
  it("derives a non-empty multi-select into queryFilters as an array and marks active", () => {
    const specs: FilterSpec[] = [
      { key: "lifecycle", type: "multi-select", label: "Lifecycle", options: [{ label: "Active", value: "active" }] },
    ];
    const urlState = { textFilters: {}, booleanFilters: {}, multiFilters: { lifecycle: ["active", "deprecated"] } };
    const { result } = renderHook(() => useListFilters(specs, urlState));
    expect(result.current.multiValues.lifecycle).toEqual(["active", "deprecated"]);
    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
  });

  it("treats an empty multi-select as inactive and undefined in queryFilters", () => {
    const specs: FilterSpec[] = [
      { key: "lifecycle", type: "multi-select", label: "Lifecycle", options: [{ label: "Active", value: "active" }] },
    ];
    const urlState = { textFilters: {}, booleanFilters: {}, multiFilters: { lifecycle: [] } };
    const { result } = renderHook(() => useListFilters(specs, urlState));
    expect(result.current.multiValues.lifecycle).toBeUndefined();
    expect(result.current.isActive).toBe(false);
  });
});
