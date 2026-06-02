import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { OwnerLink } from "../OwnerLink";

function renderInRouter(ui: React.ReactNode) {
  return render(<MemoryRouter>{ui}</MemoryRouter>);
}

describe("OwnerLink", () => {
  it("renders the fallback when user is null", () => {
    renderInRouter(<OwnerLink user={null} />);
    expect(screen.getByText("Unknown user")).toBeInTheDocument();
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("renders the fallback when user is undefined (loading state)", () => {
    renderInRouter(<OwnerLink user={undefined} />);
    expect(screen.getByText("Unknown user")).toBeInTheDocument();
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("renders a link to /users/:id with displayName as the label", () => {
    renderInRouter(
      <OwnerLink
        user={{ id: "u-1", displayName: "Alice", email: "alice@example.com" }}
      />,
    );
    const link = screen.getByRole("link", { name: "Alice" });
    expect(link).toBeInTheDocument();
    expect(link).toHaveAttribute("href", "/users/u-1");
  });

  it("falls back to email when displayName is an empty string", () => {
    renderInRouter(
      <OwnerLink
        user={{ id: "u-2", displayName: "", email: "bob@example.com" }}
      />,
    );
    const link = screen.getByRole("link", { name: "bob@example.com" });
    expect(link).toBeInTheDocument();
    expect(link).toHaveAttribute("href", "/users/u-2");
  });
});
