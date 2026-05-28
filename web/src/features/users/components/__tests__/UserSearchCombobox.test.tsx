import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, act, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import { UserSearchCombobox } from "../UserSearchCombobox";

const USERS = [
  { id: "u1", displayName: "Alice", email: "alice@example.com" },
  { id: "u2", displayName: "Alex", email: "alex@example.com" },
];

function mockApiClient(get: ReturnType<typeof vi.fn>) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get,
    POST: vi.fn(),
    PUT: vi.fn(),
    DELETE: vi.fn(),
  } as never);
}

function renderHarness(onSelect = vi.fn()) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  const utils = render(
    <QueryClientProvider client={qc}>
      <UserSearchCombobox onSelect={onSelect} />
    </QueryClientProvider>,
  );
  return { onSelect, ...utils };
}

/**
 * Drive the input via fireEvent so the test doesn't need to coordinate
 * userEvent's async ticker with the component's debounce timer.
 */
function typeInto(input: HTMLInputElement, value: string) {
  fireEvent.focus(input);
  fireEvent.change(input, { target: { value } });
}

describe("UserSearchCombobox", () => {
  beforeEach(() => {
    // Default stub keeps fetch silent for tests that don't care.
    mockApiClient(vi.fn());
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the input with no dropdown initially", () => {
    renderHarness();
    expect(screen.getByRole("combobox")).toBeInTheDocument();
    expect(screen.queryByRole("listbox")).toBeNull();
  });

  it("does not fire a search for a 1-char query (below MIN_QUERY_LENGTH)", async () => {
    const get = vi.fn();
    mockApiClient(get);
    renderHarness();

    typeInto(screen.getByRole("combobox") as HTMLInputElement, "a");
    // Wait past the 250ms debounce — search must STILL not have fired because
    // q.length < 2 disables the query.
    await new Promise((r) => setTimeout(r, 400));
    expect(get).not.toHaveBeenCalled();
  });

  it("fires a search after debounce when q.length >= 2", async () => {
    const get = vi.fn().mockResolvedValue({ data: USERS, error: undefined });
    mockApiClient(get);
    vi.useFakeTimers();
    renderHarness();

    typeInto(screen.getByRole("combobox") as HTMLInputElement, "al");

    // Below the debounce window — search must NOT have fired yet.
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(get).not.toHaveBeenCalled();

    // Cross the debounce threshold synchronously.
    act(() => {
      vi.advanceTimersByTime(100);
    });

    // Switch back to real timers so React Query's microtask + waitFor work.
    vi.useRealTimers();

    await waitFor(() => expect(get).toHaveBeenCalledTimes(1));
    expect(get).toHaveBeenCalledWith(
      "/api/v1/organizations/users",
      expect.objectContaining({ params: { query: { q: "al", limit: 10 } } }),
    );
  });

  it("renders search results in a listbox and selecting one fires onSelect + clears the input", async () => {
    const get = vi.fn().mockResolvedValue({ data: USERS, error: undefined });
    mockApiClient(get);
    const { onSelect } = renderHarness();

    const input = screen.getByRole("combobox") as HTMLInputElement;
    typeInto(input, "al");

    // Real timers — wait for debounce + React Query roundtrip.
    const option = await screen.findByRole("option", { name: /Alice/ }, { timeout: 2000 });
    expect(screen.getByRole("listbox")).toBeInTheDocument();

    fireEvent.click(option);

    expect(onSelect).toHaveBeenCalledWith(USERS[0]);
    expect(input.value).toBe("");
  });

  it("closes the dropdown on Escape and clears the input", async () => {
    const get = vi.fn().mockResolvedValue({ data: USERS, error: undefined });
    mockApiClient(get);
    renderHarness();

    const input = screen.getByRole("combobox") as HTMLInputElement;
    typeInto(input, "al");
    await screen.findByRole("listbox", undefined, { timeout: 2000 });

    fireEvent.keyDown(input, { key: "Escape" });

    expect(screen.queryByRole("listbox")).toBeNull();
    expect(input.value).toBe("");
  });

  it("closes the dropdown on outside mousedown", async () => {
    const get = vi.fn().mockResolvedValue({ data: USERS, error: undefined });
    mockApiClient(get);
    renderHarness();

    typeInto(screen.getByRole("combobox") as HTMLInputElement, "al");
    await screen.findByRole("listbox", undefined, { timeout: 2000 });

    // Dispatch a mousedown on a node outside the combobox container.
    const outside = document.createElement("div");
    document.body.appendChild(outside);
    act(() => {
      outside.dispatchEvent(
        new MouseEvent("mousedown", { bubbles: true, cancelable: true }),
      );
    });

    expect(screen.queryByRole("listbox")).toBeNull();
    document.body.removeChild(outside);
  });
});
