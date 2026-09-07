import { describe, expect, it, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HierarchyHelpCallout } from "../HierarchyHelpCallout";

// Same sessionStorage key the component persists dismissal under (kept in sync manually — not
// exported from the component module).
const DISMISS_KEY = "catalog-hierarchy-help-dismissed";

beforeEach(() => {
  sessionStorage.clear();
});

afterEach(() => {
  sessionStorage.clear();
});

describe("HierarchyHelpCallout", () => {
  it("renders the heading and key body text", () => {
    render(<HierarchyHelpCallout />);
    expect(screen.getByText("About this view")).toBeInTheDocument();
    expect(screen.getByText(/Browse your catalog by structure/)).toBeInTheDocument();
  });

  it("collapses the body when the Hide control is clicked", async () => {
    render(<HierarchyHelpCallout />);
    const toggle = screen.getByRole("button", { name: /Hide/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText(/Browse your catalog by structure/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Show about this view/ })).toBeInTheDocument();
  });

  it("renders collapsed on mount when sessionStorage has the dismissed flag", () => {
    sessionStorage.setItem(DISMISS_KEY, "true");
    render(<HierarchyHelpCallout />);

    expect(screen.queryByText(/Browse your catalog by structure/)).not.toBeInTheDocument();
    const toggle = screen.getByRole("button", { name: /Show about this view/ });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });
});
