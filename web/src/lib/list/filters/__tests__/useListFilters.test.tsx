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
