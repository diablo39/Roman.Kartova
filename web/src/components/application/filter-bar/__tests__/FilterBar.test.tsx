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
    bind: vi.fn((key: string) => ({ value: "", onChange: vi.fn() })),
    clearAll: vi.fn(),
    isActive: false,
    queryFilters: {},
  };
}

describe("FilterBar", () => {
  it("renders a text control from the spec with its label", () => {
    render(<FilterBar specs={specs} filters={filters()} />);
    expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
  });

  it("typing calls the bound onChange", () => {
    const onChange = vi.fn();
    const f = filters({ bind: vi.fn(() => ({ value: "", onChange })) });
    render(<FilterBar specs={specs} filters={f} />);
    fireEvent.change(screen.getByRole("textbox", { name: /search teams/i }), { target: { value: "pl" } });
    expect(onChange).toHaveBeenCalledWith("pl");
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
  });

  it("throws for an unbuilt control type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "boolean", label: "X" }];
    expect(() => render(<FilterBar specs={bad} filters={filters()} />)).toThrow(/not implemented/i);
  });
});
