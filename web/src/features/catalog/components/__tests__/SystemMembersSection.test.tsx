import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const useRelationshipsListMock = vi.fn();
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: (...a: unknown[]) => useRelationshipsListMock(...a),
}));

import { SystemMembersSection } from "../SystemMembersSection";

const edge = (kind: string, id: string, displayName: string, type = "partOf") => ({
  id: `rel-${id}`,
  type,
  origin: "manual",
  createdByUserId: "u1",
  createdAt: "2026-07-22T00:00:00Z",
  createdBy: null,
  source: { kind, id, displayName },
  target: { kind: "system", id: "sys1", displayName: "Payments" },
});

function result(over: Record<string, unknown> = {}) {
  return {
    items: [],
    isLoading: false,
    isError: false,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    ...over,
  };
}

const render1 = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("SystemMembersSection", () => {
  beforeEach(() => vi.clearAllMocks());

  it("queries the incoming System relationships", () => {
    useRelationshipsListMock.mockReturnValue(result());
    render1(<SystemMembersSection systemId="sys1" />);
    expect(useRelationshipsListMock).toHaveBeenCalledWith(
      expect.objectContaining({ entityKind: "system", entityId: "sys1", direction: "incoming" }),
    );
  });

  it("lists member components with a kind badge + link (row header present)", () => {
    useRelationshipsListMock.mockReturnValue(
      result({
        items: [edge("application", "a1", "Billing App"), edge("service", "s1", "Ledger Svc")],
      }),
    );
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByRole("link", { name: "Billing App" })).toHaveAttribute(
      "href",
      "/catalog/applications/a1",
    );
    expect(screen.getByRole("link", { name: "Ledger Svc" })).toHaveAttribute(
      "href",
      "/catalog/services/s1",
    );
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084
  });

  it("filters out non-PartOf edges (read-path drift tolerance)", () => {
    useRelationshipsListMock.mockReturnValue(
      result({
        items: [
          edge("application", "a1", "Billing App"),
          edge("service", "s2", "Rogue", "dependsOn"),
        ],
      }),
    );
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText("Billing App")).toBeInTheDocument();
    expect(screen.queryByText("Rogue")).not.toBeInTheDocument();
  });

  it("shows an empty state when nothing is assigned", () => {
    useRelationshipsListMock.mockReturnValue(result({ items: [] }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText("No components assigned yet.")).toBeInTheDocument();
  });

  it("shows a loading skeleton (with a row header)", () => {
    useRelationshipsListMock.mockReturnValue(result({ isLoading: true }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084 loading branch
  });

  it("shows an error line on failure", () => {
    useRelationshipsListMock.mockReturnValue(result({ isError: true }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText(/Couldn.t load members/i)).toBeInTheDocument();
  });
});
