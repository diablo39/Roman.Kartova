import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import CatalogHierarchyPage from "../CatalogHierarchyPage";

const useCatalogHierarchyMock = vi.fn();
vi.mock("../../api/hierarchy", () => ({
  useCatalogHierarchy: () => useCatalogHierarchyMock(),
}));

vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "A", displayName: "Team Alpha" }], isLoading: false }),
}));

vi.mock("@/features/organization/api/organization", () => ({
  useOrgProfile: () => ({ data: { displayName: "Acme Corp" } }),
}));

// Same sessionStorage key the page persists expand/collapse state under (kept in sync manually —
// not exported from the page module).
const EXPAND_KEY = "catalog-hierarchy-expanded";

const HAPPY_PATH_DATA = {
  totalComponentCount: 2,
  truncated: false,
  teams: [
    {
      teamId: "A",
      componentCount: 2,
      systems: [
        { systemId: "S1", displayName: "Billing", componentCount: 1,
          members: [{ kind: "service", id: "svc1", displayName: "Invoicer" }] },
      ],
      ungrouped: { componentCount: 1, members: [{ kind: "application", id: "app1", displayName: "Legacy" }] },
    },
  ],
};

function stubHierarchy(overrides: Partial<{ isLoading: boolean; isError: boolean; data: unknown }> = {}) {
  return { isLoading: false, isError: false, data: HAPPY_PATH_DATA, ...overrides };
}

function renderPage() {
  const qc = new QueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <CatalogHierarchyPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  useCatalogHierarchyMock.mockReturnValue(stubHierarchy());
  sessionStorage.clear();
});

afterEach(() => {
  sessionStorage.clear();
});

describe("CatalogHierarchyPage", () => {
  it("renders the org root and team, and drills to a member link on expand", async () => {
    renderPage();
    expect(screen.getByText("Acme Corp")).toBeInTheDocument();
    const teamToggle = screen.getByRole("button", { name: /Team Alpha/ });
    await userEvent.click(teamToggle);
    const systemToggle = screen.getByRole("button", { name: /Billing/ });
    await userEvent.click(systemToggle);
    const memberLink = screen.getByRole("link", { name: /Invoicer/ });
    expect(memberLink).toHaveAttribute("href", "/catalog/services/svc1");
  });

  it("shows a breadcrumb reflecting the selected node", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: /Team Alpha/ }));
    // Breadcrumb region shows Org / Team once the team is selected.
    const crumb = screen.getByTestId("hierarchy-breadcrumb");
    expect(crumb).toHaveTextContent("Acme Corp");
    expect(crumb).toHaveTextContent("Team Alpha");
  });

  it("shows a breadcrumb reflecting the full ancestry when a member leaf is selected", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: /Team Alpha/ }));
    await userEvent.click(screen.getByRole("button", { name: /Billing/ }));
    const memberLink = screen.getByRole("link", { name: /Invoicer/ });
    await userEvent.click(memberLink);
    const crumb = screen.getByTestId("hierarchy-breadcrumb");
    expect(crumb).toHaveTextContent("Acme Corp");
    expect(crumb).toHaveTextContent("Team Alpha");
    expect(crumb).toHaveTextContent("Billing");
    expect(crumb).toHaveTextContent("Invoicer");
  });

  it("shows a loading state while the hierarchy query is in flight", () => {
    useCatalogHierarchyMock.mockReturnValue(stubHierarchy({ isLoading: true, data: undefined }));
    renderPage();
    expect(screen.getByText(/Loading hierarchy/)).toBeInTheDocument();
    expect(screen.queryByText("Acme Corp")).not.toBeInTheDocument();
  });

  it("shows an error state when the hierarchy query fails", () => {
    useCatalogHierarchyMock.mockReturnValue(stubHierarchy({ isError: true, data: undefined }));
    renderPage();
    expect(screen.getByText(/Could not load the catalog hierarchy/)).toBeInTheDocument();
  });

  it("shows a truncated banner when the response reports truncation", () => {
    useCatalogHierarchyMock.mockReturnValue(
      stubHierarchy({ data: { ...HAPPY_PATH_DATA, truncated: true } }),
    );
    renderPage();
    expect(screen.getByText(/some are not listed/)).toBeInTheDocument();
  });

  it("renders the org root without throwing when persisted expand state is corrupt", () => {
    // Non-array JSON — not the `string[]` the page expects. loadExpanded must fall back to the
    // default rather than propagate a bad JSON.parse cast into the render.
    sessionStorage.setItem(EXPAND_KEY, JSON.stringify({ not: "an array" }));
    expect(() => renderPage()).not.toThrow();
    expect(screen.getByRole("button", { name: /Acme Corp/ })).toBeInTheDocument();
  });

  it("renders the org root without throwing when persisted expand state is a bare string", () => {
    sessionStorage.setItem(EXPAND_KEY, JSON.stringify("garbage"));
    expect(() => renderPage()).not.toThrow();
    expect(screen.getByRole("button", { name: /Acme Corp/ })).toBeInTheDocument();
  });
});
