import { it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import * as api from "@/features/catalog/api/relationships";

beforeEach(() => vi.useFakeTimers());
afterEach(() => { vi.runOnlyPendingTimers(); vi.useRealTimers(); vi.restoreAllMocks(); });

function mockSearch(items: api.EntityOption[]) {
  vi.spyOn(api, "useEntitySearch").mockReturnValue({
    data: items, isLoading: false, isError: false,
  } as never);
}

it("does not query until 2 characters are typed", () => {
  const spy = vi.spyOn(api, "useEntitySearch").mockReturnValue({ data: undefined, isLoading: false, isError: false } as never);
  render(<EntitySearchCombobox kind="service" onSelect={vi.fn()} />);
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "a" } });
  act(() => vi.advanceTimersByTime(300));
  // last call's enabled flag is false for a single char
  const lastCall = spy.mock.calls.at(-1);
  expect(lastCall?.[2]).toEqual({ enabled: false });
});

it("selecting an option fires onSelect", async () => {
  mockSearch([{ kind: "service", id: "s9", displayName: "AuthService" }]);
  const onSelect = vi.fn();
  render(<EntitySearchCombobox kind="service" onSelect={onSelect} />);
  fireEvent.focus(screen.getByRole("combobox"));
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "auth" } });
  await act(async () => { vi.advanceTimersByTime(300); });
  fireEvent.click(screen.getByText("AuthService"));
  expect(onSelect).toHaveBeenCalledWith({ kind: "service", id: "s9", displayName: "AuthService" });
});

it("excludes the excludeId from results", async () => {
  mockSearch([
    { kind: "service", id: "self", displayName: "Me" },
    { kind: "service", id: "s9", displayName: "AuthService" },
  ]);
  render(<EntitySearchCombobox kind="service" excludeId="self" onSelect={vi.fn()} />);
  fireEvent.focus(screen.getByRole("combobox"));
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "se" } });
  await act(async () => { vi.advanceTimersByTime(300); });
  expect(screen.getByText("AuthService")).toBeInTheDocument();
  expect(screen.queryByText("Me")).not.toBeInTheDocument();
});
