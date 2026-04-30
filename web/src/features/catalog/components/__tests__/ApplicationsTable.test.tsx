import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ApplicationsTable } from "../ApplicationsTable";

function withRouter(ui: React.ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const a1 = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  name: "n1",
  displayName: "App One",
  description: "first app",
  ownerUserId: "u",
  createdAt: "2026-04-30T00:00:00Z",
};
const a2 = { ...a1, id: "00000000-0000-0000-0000-000000000002", name: "n2", displayName: "App Two", description: "second" };

describe("ApplicationsTable", () => {
  it("renders rows with displayName and name", () => {
    render(withRouter(<ApplicationsTable isLoading={false} applications={[a1, a2]} />));
    expect(screen.getByText("App One")).toBeInTheDocument();
    expect(screen.getByText("App Two")).toBeInTheDocument();
    expect(screen.getByText("n1")).toBeInTheDocument();
    expect(screen.getByText("n2")).toBeInTheDocument();
  });

  it("each row links to /catalog/applications/{id}", () => {
    render(withRouter(<ApplicationsTable isLoading={false} applications={[a1]} />));
    const link = screen.getByRole("link", { name: /app one/i });
    expect(link).toHaveAttribute("href", `/catalog/applications/${a1.id}`);
  });

  it("shows empty state when applications is empty", () => {
    render(withRouter(<ApplicationsTable isLoading={false} applications={[]} />));
    expect(screen.getByText(/no applications yet/i)).toBeInTheDocument();
  });

  it("shows skeleton rows while loading", () => {
    const { container } = render(
      withRouter(<ApplicationsTable isLoading={true} applications={undefined} />)
    );
    expect(container.querySelectorAll('[data-testid="row-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders nothing-special when loading and applications is undefined", () => {
    // just a smoke pass — the loading branch should not throw on undefined data.
    render(withRouter(<ApplicationsTable isLoading={true} applications={undefined} />));
    expect(screen.queryByText(/no applications yet/i)).not.toBeInTheDocument();
  });
});
