import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";

import { CenteredSpinner } from "../CenteredSpinner";

describe("CenteredSpinner", () => {
  it("renders the supplied message as visible text", () => {
    render(<CenteredSpinner message="Signing you in…" />);
    expect(screen.getByText(/signing you in…/i)).toBeInTheDocument();
  });

  it("exposes the message via aria-label on the status region", () => {
    render(<CenteredSpinner message="Almost there…" />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-label", "Almost there…");
  });

  it("falls back to a default message when none is supplied", () => {
    render(<CenteredSpinner />);
    expect(screen.getByText(/loading…/i)).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveAttribute("aria-label", "Loading…");
  });
});
