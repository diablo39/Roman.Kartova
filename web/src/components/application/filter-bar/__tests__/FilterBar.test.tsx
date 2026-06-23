import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FilterBar } from "../FilterBar";
import type { FilterSpec } from "@/lib/list/filters/types";

// ---------------------------------------------------------------------------
// Fake urlState helpers
// ---------------------------------------------------------------------------

function fakeUrlState(
  textFilters: Record<string, string> = {},
  booleanFilters: Record<string, boolean> = {},
  setFilters = vi.fn(),
  multiFilters: Record<string, string[]> = {},
) {
  return { textFilters, booleanFilters, multiFilters, setFilters };
}

const textSpecs: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

const boolSpecs: FilterSpec[] = [
  { key: "includeDecommissioned", type: "boolean", label: "Show decommissioned" },
];

const mixedSpecs: FilterSpec[] = [...textSpecs, ...boolSpecs];

// ---------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------

describe("FilterBar — rendering", () => {
  it("renders a text input with the spec label and maxlength 128", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    const input = screen.getByRole("textbox", { name: /search teams/i });
    expect(input).toBeInTheDocument();
    expect(input).toHaveAttribute("maxlength", "128");
  });

  it("renders a boolean spec as a checkbox with the spec label", () => {
    render(<FilterBar specs={boolSpecs} urlState={fakeUrlState({}, { includeDecommissioned: false })} />);
    expect(screen.getByRole("checkbox", { name: /show decommissioned/i })).toBeInTheDocument();
  });

  it("renders a Search button", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    expect(screen.getByRole("button", { name: /^search$/i })).toBeInTheDocument();
  });

  it("is expanded by default — form controls are visible", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    expect(screen.getByRole("search")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
  });

  it("text input has defaultValue from committed textFilters", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({ displayNameContains: "hello" })} />);
    expect(screen.getByRole("textbox", { name: /search teams/i })).toHaveValue("hello");
  });

  it("checkbox is checked when committed boolean is true", () => {
    render(<FilterBar specs={boolSpecs} urlState={fakeUrlState({}, { includeDecommissioned: true })} />);
    expect(screen.getByRole("checkbox", { name: /show decommissioned/i })).toBeChecked();
  });

  it("throws for the still-reserved date-range type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "date-range", label: "X" }];
    expect(() => render(<FilterBar specs={bad} urlState={fakeUrlState()} />)).toThrow(/not implemented/i);
  });
});

// ---------------------------------------------------------------------------
// Collapsible behaviour
// ---------------------------------------------------------------------------

describe("FilterBar — collapsible", () => {
  it("toggle button has aria-expanded=true by default", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    const toggle = screen.getByRole("button", { name: /^filters/i });
    expect(toggle).toHaveAttribute("aria-expanded", "true");
  });

  it("collapsing hides the form and flips aria-expanded to false", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    const toggle = screen.getByRole("button", { name: /^filters/i });
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("search")).toBeNull();
  });

  it("re-expanding shows the form again", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    const toggle = screen.getByRole("button", { name: /^filters/i });
    fireEvent.click(toggle); // collapse
    fireEvent.click(toggle); // expand
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("search")).toBeInTheDocument();
  });

  it("collapsed header still shows active count when filters are active", () => {
    render(
      <FilterBar
        specs={textSpecs}
        urlState={fakeUrlState({ displayNameContains: "pl" })}
      />,
    );
    const toggle = screen.getByRole("button", { name: /filters \(1 active\)/i });
    fireEvent.click(toggle); // collapse
    // After collapse the header toggle button should still carry the active count.
    expect(screen.getByRole("button", { name: /filters \(1 active\)/i })).toBeInTheDocument();
  });

  it("shows 'Filters (N active)' in header when N filters are active", () => {
    render(
      <FilterBar
        specs={mixedSpecs}
        urlState={fakeUrlState({ displayNameContains: "pl" }, { includeDecommissioned: true })}
      />,
    );
    expect(screen.getByRole("button", { name: /filters \(2 active\)/i })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Submit paths: Search button + Enter key
// ---------------------------------------------------------------------------

describe("FilterBar — Search button calls setFilters", () => {
  it("clicking Search calls urlState.setFilters", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    fireEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledTimes(1);
  });

  it("clicking Search passes the current input value as text", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    const input = screen.getByRole("textbox", { name: /search teams/i });
    fireEvent.change(input, { target: { value: "pay" } });
    fireEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ text: expect.objectContaining({ displayNameContains: "pay" }) }),
    );
  });

  it("clicking Search includes boolean values", () => {
    const setFilters = vi.fn();
    render(
      <FilterBar
        specs={mixedSpecs}
        urlState={fakeUrlState({ displayNameContains: "" }, { includeDecommissioned: false }, setFilters)}
      />,
    );
    const cb = screen.getByRole("checkbox", { name: /show decommissioned/i });
    fireEvent.click(cb); // check it
    fireEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({
        booleans: expect.objectContaining({ includeDecommissioned: true }),
      }),
    );
  });
});

describe("FilterBar — Enter key calls setFilters", () => {
  it("pressing Enter in the text input calls urlState.setFilters", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    const input = screen.getByRole("textbox", { name: /search teams/i });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(setFilters).toHaveBeenCalledTimes(1);
  });

  it("Enter passes the typed text value", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    const input = screen.getByRole("textbox", { name: /search teams/i });
    fireEvent.change(input, { target: { value: "plat" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ text: expect.objectContaining({ displayNameContains: "plat" }) }),
    );
  });

  it("non-Enter keydown does NOT call setFilters", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    const input = screen.getByRole("textbox", { name: /search teams/i });
    fireEvent.keyDown(input, { key: "a" });
    expect(setFilters).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Clear all
// ---------------------------------------------------------------------------

describe("FilterBar — Clear all", () => {
  it("shows 'Clear all' only when at least one filter is active", () => {
    render(
      <FilterBar
        specs={textSpecs}
        urlState={fakeUrlState({ displayNameContains: "x" })}
      />,
    );
    expect(screen.getByRole("button", { name: /clear all/i })).toBeInTheDocument();
  });

  it("hides 'Clear all' when no filters are active", () => {
    render(<FilterBar specs={textSpecs} urlState={fakeUrlState()} />);
    expect(screen.queryByRole("button", { name: /clear all/i })).toBeNull();
  });

  it("clicking 'Clear all' calls setFilters with empty text and false booleans", () => {
    const setFilters = vi.fn();
    render(
      <FilterBar
        specs={mixedSpecs}
        urlState={fakeUrlState({ displayNameContains: "x" }, { includeDecommissioned: true }, setFilters)}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /clear all/i }));
    expect(setFilters).toHaveBeenCalledWith({
      text: { displayNameContains: "" },
      booleans: { includeDecommissioned: false },
      multi: {},
    });
  });
});

// ---------------------------------------------------------------------------
// Single-select
// ---------------------------------------------------------------------------

const selectSpecs: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
      { label: "OrgAdmin", value: "OrgAdmin" },
    ],
  },
];

// The react-aria Select trigger is the only button with aria-haspopup="listbox";
// its accessible name is "<value> <label>", so locate it structurally (not by an
// exact { name: "Role" }, which would never match "All roles Role").
const selectTrigger = () =>
  screen.getAllByRole("button").find(b => b.getAttribute("aria-haspopup") === "listbox") as HTMLElement;

describe("FilterBar — single-select", () => {
  it("renders a Select for a single-select spec", () => {
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState()} />);
    expect(selectTrigger()).toBeInTheDocument();
  });

  it("choosing a value + Search commits it via setFilters (text map)", async () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    await userEvent.click(selectTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "OrgAdmin" }));
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ text: expect.objectContaining({ role: "OrgAdmin" }) }),
    );
  });

  it("seeds the Select from the committed value", () => {
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState({ role: "Viewer" })} />);
    expect(selectTrigger()).toHaveTextContent("Viewer");
  });

  it("Clear all resets a committed single-select to empty", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState({ role: "OrgAdmin" }, {}, setFilters)} />);
    fireEvent.click(screen.getByRole("button", { name: /clear all/i }));
    expect(setFilters).toHaveBeenCalledWith({ text: { role: "" }, booleans: {}, multi: {} });
  });
});

// ---------------------------------------------------------------------------
// Multi-select
// ---------------------------------------------------------------------------

const multiSpecs: FilterSpec[] = [
  {
    key: "lifecycle",
    type: "multi-select",
    label: "Lifecycle",
    placeholder: "Any status",
    options: [
      { label: "Active", value: "active" },
      { label: "Deprecated", value: "deprecated" },
      { label: "Decommissioned", value: "decommissioned" },
    ],
  },
];
// The multi-select trigger is the only non-toggle, non-Search button; query it via its label text.
const multiTrigger = () => screen.getByRole("button", { name: /lifecycle/i });

describe("FilterBar — multi-select", () => {
  it("renders a MultiSelect for a multi-select spec", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState()} />);
    expect(multiTrigger()).toBeInTheDocument();
  });

  it("seeds the MultiSelect from committed multiFilters", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, vi.fn(), { lifecycle: ["active", "deprecated"] })} />);
    expect(multiTrigger()).toHaveTextContent("2 selected");
  });

  it("choosing values + Search commits them via setFilters (multi map)", async () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    await userEvent.click(multiTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "Deprecated" }));
    // Close the popover (multi-select stays open after selection; popover traps
    // ARIA focus). Click document.body to dismiss, bypassing ARIA modal.
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ multi: expect.objectContaining({ lifecycle: ["deprecated"] }) }),
    );
  });

  it("Clear all resets a committed multi-select to empty", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, setFilters, { lifecycle: ["active"] })} />);
    fireEvent.click(screen.getByRole("button", { name: /clear all/i }));
    expect(setFilters).toHaveBeenCalledWith({ text: {}, booleans: {}, multi: { lifecycle: [] } });
  });

  it("counts a non-empty multi-select toward the active-filter header", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, vi.fn(), { lifecycle: ["active"] })} />);
    expect(screen.getByRole("button", { name: /filters \(1 active\)/i })).toBeInTheDocument();
  });
});
