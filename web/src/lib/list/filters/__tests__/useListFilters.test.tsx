import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
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

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("useListFilters", () => {
  it("debounces the commit to setTextFilter (300ms)", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    expect(u.setTextFilter).not.toHaveBeenCalled();        // immediate value only
    expect(result.current.values.displayNameContains).toBe("pl");

    act(() => { vi.advanceTimersByTime(300); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "pl");
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

  it("isActive false when all committed values are empty", () => {
    const empty = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: empty.state.textFilters, setTextFilter: empty.setTextFilter } as never));
    expect(result.current.isActive).toBe(false);
  });

  it("isActive true when a committed value is non-empty", () => {
    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    expect(result.current.isActive).toBe(true);
  });

  it("activeCount reflects the number of non-empty committed filters", () => {
    const empty = fakeUrlState();
    const { result: r1 } = renderHook(() =>
      useListFilters(specs, { textFilters: empty.state.textFilters, setTextFilter: empty.setTextFilter } as never));
    expect(r1.current.activeCount).toBe(0);

    const u = fakeUrlState({ displayNameContains: "pl" });
    const { result: r2 } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));
    expect(r2.current.activeCount).toBe(1);
  });

  it("debounce boundary: fires at exactly 300ms, not before", () => {
    const u = fakeUrlState();
    const { result } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    act(() => { vi.advanceTimersByTime(299); });
    expect(u.setTextFilter).not.toHaveBeenCalled();

    act(() => { vi.advanceTimersByTime(1); });
    expect(u.setTextFilter).toHaveBeenCalledWith("displayNameContains", "pl");
  });

  it("unmount cancels a pending debounced commit", () => {
    const u = fakeUrlState();
    const { result, unmount } = renderHook(() =>
      useListFilters(specs, { textFilters: u.state.textFilters, setTextFilter: u.setTextFilter } as never));

    act(() => { result.current.bind("displayNameContains").onChange("pl"); });
    unmount();
    act(() => { vi.advanceTimersByTime(300); });
    expect(u.setTextFilter).not.toHaveBeenCalled();
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
