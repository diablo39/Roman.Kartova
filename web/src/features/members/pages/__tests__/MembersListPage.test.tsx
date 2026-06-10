import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { MembersListPage } from "../MembersListPage";

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

describe("MembersListPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
  });

  it("renders member row with Change role and Remove buttons for full-access caller", async () => {
    mockPermissions([
      KartovaPermissions.OrgUsersRead,
      KartovaPermissions.OrgUsersRoleChange,
      KartovaPermissions.OrgUsersRemove,
    ]);

    const get = vi.fn().mockResolvedValue({
      data: pageOf([
        {
          id: "u1",
          displayName: "Bob",
          email: "bob@x.io",
          role: "Member",
          teamCount: 1,
          lastSeenAt: null,
          createdAt: "2026-01-01T00:00:00Z",
        },
      ]),
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get,
      POST: vi.fn(),
      PUT: vi.fn(),
      DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<MembersListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText("Bob")).toBeInTheDocument());
    expect(screen.getByText("Member")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /change role/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /remove/i })).toBeInTheDocument();
  });

  it("hides Change role and Remove buttons when caller only has OrgUsersRead", async () => {
    mockPermissions([KartovaPermissions.OrgUsersRead]);

    const get = vi.fn().mockResolvedValue({
      data: pageOf([
        {
          id: "u1",
          displayName: "Bob",
          email: "bob@x.io",
          role: "Member",
          teamCount: 1,
          lastSeenAt: null,
          createdAt: "2026-01-01T00:00:00Z",
        },
      ]),
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get,
      POST: vi.fn(),
      PUT: vi.fn(),
      DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<MembersListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText("Bob")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /change role/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /remove/i })).toBeNull();
  });
});
