import React from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";

const useAuthMock = vi.fn();

vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { usePermissions } from "../usePermissions";
import { KartovaPermissions } from "../permissions";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function newQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

describe("usePermissions", () => {
  beforeEach(() => {
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "test-token" },
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    useAuthMock.mockReset();
  });

  it("returns Viewer set when API returns Viewer role", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { role: "Viewer", permissions: ["catalog.read"], teamMemberships: [] },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.role).toBe("Viewer");
    expect(result.current.hasPermission(KartovaPermissions.CatalogRead)).toBe(true);
    expect(result.current.hasPermission(KartovaPermissions.CatalogApplicationsRegister)).toBe(
      false,
    );
  });

  it("returns OrgAdmin set with all permissions", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        role: "OrgAdmin",
        permissions: [
          "catalog.read",
          "catalog.applications.register",
          "catalog.applications.edit-metadata",
          "catalog.applications.lifecycle.forward",
          "catalog.applications.lifecycle.reverse",
          "team.read",
          "team.create",
          "org.profile.read",
          "org.profile.edit",
          "org.invitations.read",
          "org.invitations.create",
          "org.invitations.revoke",
          "org.users.read",
          "org.users.search",
          "org.users.role.change",
          "org.users.remove",
        ],
        teamMemberships: [],
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.role).toBe("OrgAdmin");
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(true);
    }
  });

  it("isLoading is true initially before the fetch resolves", () => {
    const get = vi.fn(() => new Promise(() => { /* never resolves */ }));
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    expect(result.current.isLoading).toBe(true);
    expect(result.current.hasPermission(KartovaPermissions.CatalogRead)).toBe(false);
  });

  it("returns false for all permissions on 401", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 401, title: "Unauthorized" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.isError).toBe(true);
    expect(result.current.role).toBeNull();
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(false);
    }
  });

  it("sets isError to true on 403 response", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 403, title: "Forbidden" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), { wrapper: makeWrapper(newQueryClient()) });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.isError).toBe(true);
    expect(result.current.role).toBeNull();
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(false);
    }
  });

  it("returns teamIds and teamAdminTeamIds from /me/permissions", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        role: "Member",
        permissions: ["catalog.read", "team.read"],
        teamMemberships: [
          { teamId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", role: "Admin" },
          { teamId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", role: "Member" },
        ],
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.teamIds).toEqual([
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    ]);
    expect(result.current.teamAdminTeamIds).toEqual([
      "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    ]);
  });

  it("returns empty teamIds and teamAdminTeamIds when user has no memberships", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { role: "Viewer", permissions: ["catalog.read"], teamMemberships: [] },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.teamIds).toEqual([]);
    expect(result.current.teamAdminTeamIds).toEqual([]);
  });
});
