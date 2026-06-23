import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useListFilters } from "../useListFilters";
import type { FilterSpec } from "../types";

const specs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

function fakeUrlState(initial: Record<string, string> = {}) {
  const state = { textFilters: { displayNameContains: "", ...initial } };
  const setTextFilter = vi.fn((name: string, value: string) => {
    state.textFilters = { ...state.textFilters, [name]: value.trim() };
  });
  return { state, setTextFilter };
}

describe("useListFilters", () => {
  it("typing via bind(key).onChange updates values (draft) but does NOT call setTextFilter", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    expect(u.setTextFilter).not.toHaveBeenCalled();
    expect(result.current.values.displayNameContains).toBe("pl");
  });

  it("submit() calls setTextFilter with the draft value", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    expect(u.setTextFilter).not.toHaveBeenCalled();

    act(() => { result.current.submit(); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "pl");
  });

  it("clearAll() clears the draft and calls setTextFilter with empty string", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("abc"); });
    act(() => { result.current.clearAll(); });

    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "");
    expect(result.current.values.displayNameContains).toBe("");
  });

  it("isActive/activeCount/queryFilters reflect committed (not draft)", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
    expect(result.current.queryFilters.displayNameContains).toBe("pl");
  });

  it("isActive false when committed is empty even if draft is non-empty", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("draft"); });
    expect(result.current.isActive).toBe(false);
    expect(result.current.activeCount).toBe(0);
    expect(result.current.queryFilters.displayNameContains).toBeUndefined();
  });

  it("queryFilters reflects committed values; undefined when empty", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    expect(result.current.queryFilters.displayNameContains).toBe("pl");

    const empty = fakeUrlState();
    const { result: r2 } = renderHook(() =>
      useListFilters(specs, { textFilters: empty.state.textFilters, setTextFilter: empty.setTextFilter } as never));
    expect(r2.current.queryFilters.displayNameContains).toBeUndefined();
  });

  it("clearAll resets every spec via setTextFilter('')", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    act(() => { result.current.clearAll(); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "");
    expect(result.current.values.displayNameContains).toBe("");
  });
});

function fakeUrlStateB(text: Record<string, string> = {}, bool: Record<string, boolean> = {}) {
  const state = {
    textFilters: { ...text } as Record<string, string>,
    booleanFilters: { ...bool } as Record<string, boolean>,
  };
  const setTextFilter = vi.fn((name: string, value: string) => {
    state.textFilters = { ...state.textFilters, [name]: value.trim() };
  });
  const setBooleanFilter = vi.fn((name: string, value: boolean) => {
    state.booleanFilters = { ...state.booleanFilters, [name]: value };
  });
  return { state, setTextFilter, setBooleanFilter };
}

const boolSpecs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search", placeholder: "…" },
  { key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" },
];

describe("useListFilters — boolean specs", () => {
  it("bindBoolean toggling updates draft but does NOT commit until submit", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: false });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));

    act(() => { result.current.bindBoolean("includeDecommissioned").onChange(true); });
    expect(u.setBooleanFilter).not.toHaveBeenCalled();
    expect(result.current.bindBoolean("includeDecommissioned").value).toBe(true);

    act(() => { result.current.submit(); });
    expect(u.setBooleanFilter).toHaveBeenCalledWith("includeDecommissioned", true);
  });

  it("queryFilters carries the committed boolean (always present)", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    expect(result.current.queryFilters.includeDecommissioned).toBe(true);
  });

  it("isActive/activeCount count a committed true boolean", () => {
    const u = fakeUrlStateB({ displayNameContains: "" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
  });

  it("clearAll resets booleans to false and commits", () => {
    const u = fakeUrlStateB({ displayNameContains: "x" }, { includeDecommissioned: true });
    const { result } = renderHook(() => useListFilters(boolSpecs, {
      textFilters: u.state.textFilters, setTextFilter: u.setTextFilter,
      booleanFilters: u.state.booleanFilters, setBooleanFilter: u.setBooleanFilter,
    } as never));
    act(() => { result.current.clearAll(); });
    expect(u.setBooleanFilter).toHaveBeenCalledWith("includeDecommissioned", false);
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "");
  });
});

// ---------------------------------------------------------------------------
// External-URL reconcile: render-time re-seed when committed value changes
// from outside (e.g. back/forward navigation).
// ---------------------------------------------------------------------------

describe("useListFilters — external URL reconcile", () => {
  it("(text) re-seeds draft when committed value changes from outside", () => {
    // Start with empty committed text. The draft should also be empty.
    let textFilters: Record<string, string> = { displayNameContains: "" };
    const setTextFilter = vi.fn();
    const { result, rerender } = renderHook(
      (props: { textFilters: Record<string, string> }) =>
        useListFilters(specs, { textFilters: props.textFilters, setTextFilter } as never),
      { initialProps: { textFilters } },
    );

    expect(result.current.bind("displayNameContains").value).toBe("");

    // Simulate back-nav / external URL change: new object identity + new value.
    textFilters = { displayNameContains: "plat" };
    rerender({ textFilters });

    expect(result.current.bind("displayNameContains").value).toBe("plat");
    expect(result.current.values.displayNameContains).toBe("plat");
  });

  it("(boolean) re-seeds boolDraft when committed boolean changes from outside", () => {
    const setBooleanFilter = vi.fn();
    const setTextFilter = vi.fn();
    // Pass both textFilters and booleanFilters as props so neither creates new objects in the hook closure.
    const { result, rerender } = renderHook(
      (props: { textFilters: Record<string, string>; booleanFilters: Record<string, boolean> }) =>
        useListFilters(boolSpecs, {
          textFilters: props.textFilters,
          setTextFilter,
          booleanFilters: props.booleanFilters,
          setBooleanFilter,
        } as never),
      { initialProps: { textFilters: { displayNameContains: "" }, booleanFilters: { includeDecommissioned: false } } },
    );

    expect(result.current.bindBoolean("includeDecommissioned").value).toBe(false);

    // Simulate external change (back-nav, shared URL): new object identity + new value.
    rerender({ textFilters: { displayNameContains: "" }, booleanFilters: { includeDecommissioned: true } });

    expect(result.current.bindBoolean("includeDecommissioned").value).toBe(true);
  });
});
