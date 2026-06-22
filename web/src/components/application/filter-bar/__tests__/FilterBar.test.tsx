import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { FilterBar } from "../FilterBar";
import type { FilterSpec } from "@/lib/list/filters/types";

const specs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

function filters(over: Partial<ReturnType<typeof makeFilters>> = {}) {
  return { ...makeFilters(), ...over };
}
function makeFilters() {
  return {
    values: { displayNameContains: "" },
    bind: vi.fn((_key: string) => ({ value: "", onChange: vi.fn() })),
    bindBoolean: vi.fn((_key: string) => ({ value: false, onChange: vi.fn() })),
    submit: vi.fn(),
    clearAll: vi.fn(),
    isActive: false,
    activeCount: 0,
    queryFilters: {},
  };
}

describe("FilterBar", () => {
  it("renders a text control from the spec with its label", () => {
    render(<FilterBar specs={specs} filters={filters()} />);
    expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: /search teams/i })).toHaveAttribute("maxlength", "128");
  });

  it("renders a Search button", () => {
    render(<FilterBar specs={specs} filters={filters()} />);
    expect(screen.getByRole("button", { name: /search/i })).toBeInTheDocument();
  });

  it("typing calls the bound onChange (draft)", () => {
    const onChange = vi.fn();
    const f = filters({ bind: vi.fn(() => ({ value: "", onChange })) });
    render(<FilterBar specs={specs} filters={f} />);
    fireEvent.change(screen.getByRole("textbox", { name: /search teams/i }), { target: { value: "pl" } });
    expect(onChange).toHaveBeenCalledWith("pl");
    expect(f.bind).toHaveBeenCalledWith("displayNameContains");
  });

  it("submitting the form calls filters.submit", () => {
    const f = filters();
    render(<FilterBar specs={specs} filters={f} />);
    const form = screen.getByRole("search");
    fireEvent.submit(form);
    expect(f.submit).toHaveBeenCalled();
  });

  it("clicking Search button calls filters.submit", () => {
    const f = filters();
    render(<FilterBar specs={specs} filters={f} />);
    fireEvent.click(screen.getByRole("button", { name: /search/i }));
    expect(f.submit).toHaveBeenCalled();
  });

  it("pressing Enter in the text input calls filters.submit", () => {
    const f = filters();
    render(<FilterBar specs={specs} filters={f} />);
    const textbox = screen.getByRole("textbox", { name: /search teams/i });
    fireEvent.keyDown(textbox, { key: "Enter" });
    expect(f.submit).toHaveBeenCalled();
  });

  it("shows Clear all only when active and calls clearAll", () => {
    const f = filters({ isActive: true });
    render(<FilterBar specs={specs} filters={f} />);
    const btn = screen.getByRole("button", { name: /clear all/i });
    fireEvent.click(btn);
    expect(f.clearAll).toHaveBeenCalled();
  });

  it("hides Clear all when inactive", () => {
    render(<FilterBar specs={specs} filters={filters({ isActive: false })} />);
    expect(screen.queryByRole("button", { name: /clear all/i })).toBeNull();
    expect(screen.queryByText(/active/i)).toBeNull();
  });

  it("shows active-filter count alongside Clear all when active", () => {
    const f = filters({
      isActive: true,
      activeCount: 1,
      queryFilters: { displayNameContains: "pl" },
    });
    render(<FilterBar specs={specs} filters={f} />);
    expect(screen.getByText(/1 active/i)).toBeInTheDocument();
  });

  it("throws for an unbuilt control type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "boolean", label: "X" }];
    expect(() => render(<FilterBar specs={bad} filters={filters()} />)).toThrow(/not implemented/i);
  });
});
