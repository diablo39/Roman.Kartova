import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";

const api = {
  id: "a1", tenantId: "t", displayName: "Orders API", description: "Order management",
  style: "graphQL", version: "v2", specUrl: "https://example.com/spec.json", teamId: "team1",
  createdByUserId: "u1", createdAt: "2026-07-04T10:00:00Z", createdBy: null, hasSpec: false,
};
vi.mock("@/features/catalog/api/apis", () => ({
  useApi: () => ({ data: api, isLoading: false, isError: false }),
  useApiSpec: () => ({ data: null, isLoading: false, isError: false }),
  useUpsertApiSpec: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isLoading: false, isError: false }),
}));
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: () => ({ items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: () => {}, goPrev: () => {} }),
  useDeleteRelationship: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: () => false, role: "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false }),
}));

import { ApiDetailPage } from "../ApiDetailPage";

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/catalog/apis/a1"]}>
      <Routes><Route path="/catalog/apis/:id" element={<ApiDetailPage />} /></Routes>
    </MemoryRouter>,
  );
}

describe("ApiDetailPage", () => {
  it("renders name, style label, version and a spec-url external link", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: "Orders API" })).toBeInTheDocument();
    expect(screen.getByText("GraphQL")).toBeInTheDocument();
    expect(screen.getByText("v2")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /spec/i });
    expect(link).toHaveAttribute("href", "https://example.com/spec.json");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("mounts a read-only incoming relationships section", () => {
    renderPage();
    expect(screen.getByText("Incoming")).toBeInTheDocument();
    expect(screen.queryByText("Outgoing")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /add/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });
});
