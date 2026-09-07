import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import CatalogHierarchyPage from "../CatalogHierarchyPage";

vi.mock("../../api/hierarchy", () => ({
  useCatalogHierarchy: () => ({
    isLoading: false,
    isError: false,
    data: {
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
    },
  }),
}));

vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "A", displayName: "Team Alpha" }], isLoading: false }),
}));

vi.mock("@/features/organization/api/organization", () => ({
  useOrgProfile: () => ({ data: { displayName: "Acme Corp" } }),
}));

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
});
