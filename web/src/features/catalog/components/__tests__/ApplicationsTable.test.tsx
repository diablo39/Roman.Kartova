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
  displayName: "App One",
  description: "first app",
  ownerUserId: "00000000-0000-0000-0000-0000000000aa",
  owner: {
    id: "00000000-0000-0000-0000-0000000000aa",
    displayName: "Alice Admin",
    email: "alice@example.com",
  },
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
};
const a2: ApplicationRow = {
  ...a1,
  id: "00000000-0000-0000-0000-000000000002",
  displayName: "App Two",
  description: "second",
  // Intentionally omit `owner` here so the "Unknown user" fallback is exercised
  // in the multi-row render test.
  owner: null,
};

function makeList(overrides: Partial<CursorListResult<ApplicationRow>>): CursorListResult<ApplicationRow> {
  return {
    items: [],
    isLoading: false,
    isFetching: false,
    isError: false,
    error: null,
    hasNext: false,
    hasPrev: false,
    goNext: () => {},
    goPrev: () => {},
    reset: () => {},
    refetch: () => {},
    ...overrides,
  };
}

const noop = () => {};
const emptyTeamMap = new Map<string, string>();

describe("ApplicationsTable", () => {
  it("renders rows with displayName", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1, a2] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.getByText("App One")).toBeInTheDocument();
    expect(screen.getByText("App Two")).toBeInTheDocument();
  });

  it("each row links to /catalog/applications/{id}", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
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
        teamNameById={emptyTeamMap}
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
          teamNameById={emptyTeamMap}
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
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.queryByText(/no applications yet/i)).not.toBeInTheDocument();
  });

  it("renders 'Unassigned' for rows with no teamId", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.getByText(/unassigned/i)).toBeInTheDocument();
  });

  it("renders team displayName as a link when teamId resolves", () => {
    const teamed: ApplicationRow = { ...a1, teamId: "team-xyz" };
    const names = new Map<string, string>([["team-xyz", "Platform"]]);
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [teamed] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={names}
      />
    ));
    const link = screen.getByRole("link", { name: /^platform$/i });
    expect(link).toHaveAttribute("href", "/teams/team-xyz");
  });

  it("falls back to 'Unknown team' when teamId is not in the resolver map", () => {
    const teamed: ApplicationRow = { ...a1, teamId: "ghost" };
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [teamed] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.getByText(/unknown team/i)).toBeInTheDocument();
  });

  it("renders the owner display name as a link to /users/{id} (slice-9 F8)", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    const ownerLink = screen.getByRole("link", { name: /alice admin/i });
    expect(ownerLink).toHaveAttribute("href", "/users/00000000-0000-0000-0000-0000000000aa");
  });

  it("renders 'Unknown user' fallback when owner is null (slice-9 F8)", () => {
    // a2 has owner: null — the OwnerLink component renders an italic fallback.
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a2] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.getByText(/unknown user/i)).toBeInTheDocument();
  });

  it("renders an Owner column header", () => {
    render(withRouter(
      <ApplicationsTable
        list={makeList({ items: [a1] })}
        sortBy="createdAt"
        sortOrder="desc"
        onSortChange={noop}
        teamNameById={emptyTeamMap}
      />
    ));
    expect(screen.getByRole("columnheader", { name: /owner/i })).toBeInTheDocument();
  });
});
