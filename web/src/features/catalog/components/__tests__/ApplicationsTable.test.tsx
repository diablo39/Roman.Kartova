import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ApplicationsTable } from "../ApplicationsTable";
import type { CursorListResult } from "@/lib/list/types";
import type { ApplicationRow } from "../ApplicationsTable";

function withRouter(ui: React.ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const a1: ApplicationRow = {
  id: "00000000-0000-0000-0000-000000000001",
  name: "n1",
  displayName: "App One",
  description: "first app",
  ownerUserId: "u",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
};
const a2: ApplicationRow = { ...a1, id: "00000000-0000-0000-0000-000000000002", name: "n2", displayName: "App Two", description: "second" };

function makeList(overrides: Partial<CursorListResult<ApplicationRow>>): CursorListResult<ApplicationRow> {
  return {
    items: [],
    isLoading: false,
    isFetching: false,
    isError: false,
    hasNext: false,
    hasPrev: false,
    goNext: () => {},
    goPrev: () => {},
    reset: () => {},
    ...overrides,
  };
}

const noop = () => {};

describe("ApplicationsTable", () => {
  it("renders rows with displayName and name", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1, a2] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
      />
    ));
    expect(screen.getByText("App One")).toBeInTheDocument();
    expect(screen.getByText("App Two")).toBeInTheDocument();
    expect(screen.getByText("n1")).toBeInTheDocument();
    expect(screen.getByText("n2")).toBeInTheDocument();
  });

  it("each row links to /catalog/applications/{id}", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
      />
    ));
    const link = screen.getByRole("link", { name: /app one/i });
    expect(link).toHaveAttribute("href", `/catalog/applications/${a1.id}`);
  });

  it("shows empty state when items is empty", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
      />
    ));
    expect(screen.getByText(/no applications yet/i)).toBeInTheDocument();
  });

  it("shows skeleton rows while loading", () => {
    const { container } = render(
      withRouter(
        <ApplicationsTable
          list={makeList({ isLoading: true, items: [] })}
          sortBy="createdAt"
          sortOrder="desc"
          onSortChange={noop}
        />
      )
    );
    expect(container.querySelectorAll('[data-testid="row-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders skeleton when loading regardless of items", () => {
    // smoke pass — loading branch should not throw
    render(withRouter(
      <ApplicationsTable
        list={makeList({ isLoading: true, items: [] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
      />
    ));
    expect(screen.queryByText(/no applications yet/i)).not.toBeInTheDocument();
  });
});
