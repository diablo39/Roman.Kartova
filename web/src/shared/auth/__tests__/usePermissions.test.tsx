import React from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

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

function mockFetchOnce(body: unknown, status = 200): typeof fetch {
  return vi.fn(async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    }),
  ) as typeof fetch;
}

describe("usePermissions", () => {
  let originalFetch: typeof globalThis.fetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "test-token" },
    });
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
    useAuthMock.mockReset();
  });

  it("returns Viewer set when API returns Viewer role", async () => {
    globalThis.fetch = mockFetchOnce({
      role: "Viewer",
      permissions: ["catalog.read"],
    });

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

  it("returns OrgAdmin set with all five permissions", async () => {
    globalThis.fetch = mockFetchOnce({
      role: "OrgAdmin",
      permissions: [
        "catalog.read",
        "catalog.applications.register",
        "catalog.applications.edit-metadata",
        "catalog.applications.lifecycle.forward",
        "catalog.applications.lifecycle.reverse",
      ],
    });

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
    globalThis.fetch = vi.fn(
      () =>
        new Promise(() => {
          /* never resolves */
        }),
    ) as typeof fetch;

    const { result } = renderHook(() => usePermissions(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    expect(result.current.isLoading).toBe(true);
    expect(result.current.hasPermission(KartovaPermissions.CatalogRead)).toBe(false);
  });

  it("returns false for all permissions on 401", async () => {
    globalThis.fetch = mockFetchOnce(undefined, 401);

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
    globalThis.fetch = mockFetchOnce(undefined, 403);

    const { result } = renderHook(() => usePermissions(), { wrapper: makeWrapper(newQueryClient()) });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.isError).toBe(true);
    expect(result.current.role).toBeNull();
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(false);
    }
  });
});
