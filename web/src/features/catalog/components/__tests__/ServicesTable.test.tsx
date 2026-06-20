import React from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ServicesTable } from "../ServicesTable";
import type { CursorListResult } from "@/lib/list/types";
import type { ServiceResponse } from "@/features/catalog/api/services";

function withRouter(ui: React.ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const s1: ServiceResponse = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  displayName: "Orders",
  description: "Order service",
  teamId: "00000000-0000-0000-0000-000000000010",
  createdByUserId: "00000000-0000-0000-0000-0000000000aa",
  createdBy: { id: "00000000-0000-0000-0000-0000000000aa", displayName: "Alice Admin", email: "alice@example.com" },
  createdAt: "2026-04-30T00:00:00Z",
  health: "unknown",
  endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
  version: "v1",
};

function makeList(overrides: Partial<CursorListResult<ServiceResponse>>): CursorListResult<ServiceResponse> {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: () => {}, goPrev: () => {}, reset: () => {}, refetch: () => {},
    ...overrides,
  };
}

const noop = () => {};
const teamNames = new Map<string, string>([["00000000-0000-0000-0000-000000000010", "Platform"]]);

describe("ServicesTable", () => {
  it("renders a row linking to the service detail page", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    const link = screen.getByRole("link", { name: /orders/i });
    expect(link).toHaveAttribute("href", `/catalog/services/${s1.id}`);
  });

  it("renders the health badge and endpoint count", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  it("links the team name", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByRole("link", { name: /^platform$/i })).toHaveAttribute("href", "/teams/00000000-0000-0000-0000-000000000010");
  });

  it("shows the empty state when there are no services", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByText(/no services yet/i)).toBeInTheDocument();
  });

  it("renders skeleton rows while loading", () => {
    const { container } = render(withRouter(<ServicesTable list={makeList({ isLoading: true })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(container.querySelectorAll('[data-testid="row-skeleton"]').length).toBeGreaterThan(0);
  });

  it("invokes onSortChange when the Name header is activated", async () => {
    const onSortChange = vi.fn();
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="createdAt" sortOrder="desc" onSortChange={onSortChange} teamNameById={teamNames} />));
    await userEvent.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenCalled();
  });
});
