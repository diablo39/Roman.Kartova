import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { TeamsListPage } from "../TeamsListPage";

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

import { KartovaPermissions } from "@/shared/auth/permissions";

function mockPermissions(perms: string[]) {
  usePermissionsMock.mockReturnValue({
    role: "test",
    hasPermission: (p: string) => perms.includes(p),
    isLoading: false,
    teamIds: [],
    teamAdminTeamIds: [],
  });
}

function pageOf<T>(items: T[]) {
  return { items, nextCursor: null, prevCursor: null };
}

function harness(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

describe("TeamsListPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
  });

  it("renders teams returned by the API", async () => {
    const get = vi.fn().mockResolvedValue({
      data: pageOf([
        { id: "t1", displayName: "Platform", description: "Owns infra", createdAt: "2026-01-01T00:00:00Z" },
      ]),
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<TeamsListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText("Platform")).toBeInTheDocument());
  });

  it("hides Create team when caller lacks TeamCreate", async () => {
    mockPermissions([KartovaPermissions.TeamRead]);
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<TeamsListPage />, { wrapper: harness(qc) });

    expect(screen.queryByRole("button", { name: /create team/i })).toBeNull();
  });

  it("shows Create team when caller has TeamCreate", async () => {
    mockPermissions([KartovaPermissions.TeamRead, KartovaPermissions.TeamCreate]);
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<TeamsListPage />, { wrapper: harness(qc) });

    expect(screen.getByRole("button", { name: /create team/i })).toBeInTheDocument();
  });

  it("renders the search filter and uses displayName-asc as default sort", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<TeamsListPage />, { wrapper: harness(qc) });

    expect(screen.getByRole("textbox", { name: /search teams/i })).toBeInTheDocument();
    await waitFor(() => expect(get).toHaveBeenCalled());
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    expect(get.mock.calls[0]![1]!.params.query).toMatchObject({ sortBy: "displayName", sortOrder: "asc" });
  });

  it("shows the no-matches empty state when a filter is active and no rows", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<TeamsListPage />, {
      wrapper: ({ children }: { children: React.ReactNode }) => (
        <QueryClientProvider client={qc}>
          <MemoryRouter initialEntries={["/?displayNameContains=zzz"]}>{children}</MemoryRouter>
        </QueryClientProvider>
      ),
    });

    await waitFor(() => expect(screen.getByText(/no teams match/i)).toBeInTheDocument());
  });
});
