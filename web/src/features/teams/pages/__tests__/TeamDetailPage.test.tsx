import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { TeamDetailPage } from "../TeamDetailPage";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: { access_token: "t", profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" } },
  }),
}));

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

function mockPermissions(p: { role: string; teamAdminTeamIds?: string[] }) {
  usePermissionsMock.mockReturnValue({
    role: p.role,
    hasPermission: () => true,
    isLoading: false,
    teamIds: [],
    teamAdminTeamIds: p.teamAdminTeamIds ?? [],
  });
}

const TEAM_ID = "00000000-0000-0000-0000-000000000abc";

const baseTeam = {
  id: TEAM_ID,
  displayName: "Platform",
  description: "Owns shared infra",
  createdAt: "2026-01-01T00:00:00Z",
  members: [
    { userId: "u-1", role: "Admin", addedAt: "2026-01-02T00:00:00Z" },
    { userId: "u-2", role: "Member", addedAt: "2026-01-03T00:00:00Z" },
  ],
  applications: [
    { id: "app-1", displayName: "Billing API", lifecycle: "Active" },
  ],
};

function harness(qc: QueryClient) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/teams/${TEAM_ID}`]}>
        <Routes>
          <Route path="/teams/:id" element={<TeamDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function mockTeamGet() {
  const get = vi.fn().mockResolvedValue({ data: baseTeam, error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
  } as never);
  return get;
}

describe("TeamDetailPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders header, members table and applications", async () => {
    mockPermissions({ role: "OrgAdmin" });
    mockTeamGet();
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc));

    await waitFor(() => expect(screen.getByText("Platform")).toBeInTheDocument());
    expect(screen.getByText(/owns shared infra/i)).toBeInTheDocument();
    expect(screen.getByText("u-1")).toBeInTheDocument();
    expect(screen.getByText("u-2")).toBeInTheDocument();
    expect(screen.getByText("Billing API")).toBeInTheDocument();
    expect(screen.getByText("app-1")).toBeInTheDocument();
  });

  it("hides Rename/Delete for a plain Member of the team", async () => {
    mockPermissions({ role: "Member", teamAdminTeamIds: [] });
    mockTeamGet();
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc));

    await waitFor(() => expect(screen.getByText("Platform")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /^rename$/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /^delete$/i })).toBeNull();
  });

  it("shows Rename/Delete for OrgAdmin", async () => {
    mockPermissions({ role: "OrgAdmin" });
    mockTeamGet();
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc));

    await waitFor(() => expect(screen.getByText("Platform")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /^rename$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^delete$/i })).toBeInTheDocument();
  });

  it("shows Rename/Delete for TeamAdmin of this team", async () => {
    mockPermissions({ role: "Member", teamAdminTeamIds: [TEAM_ID] });
    mockTeamGet();
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc));

    await waitFor(() => expect(screen.getByText("Platform")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /^rename$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^delete$/i })).toBeInTheDocument();
  });
});
