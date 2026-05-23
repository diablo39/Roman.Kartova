import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// Must be hoisted above the import of AppLayout so the mock is in place when
// the module is evaluated.
const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

// Sidebar and TopBar have their own auth/query dependencies; stub them so
// AppLayout tests are isolated from those concerns.
vi.mock("../Sidebar", () => ({
  Sidebar: () => <nav data-testid="sidebar" />,
}));
vi.mock("../TopBar", () => ({
  TopBar: () => <header data-testid="topbar" />,
}));

import { AppLayout } from "../AppLayout";
import { KartovaPermissions } from "@/shared/auth/permissions";

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

describe("AppLayout", () => {
  beforeEach(() => {
    usePermissionsMock.mockReset();
  });

  it("renders the protected shell (Sidebar + TopBar) when user has CatalogRead", () => {
    usePermissionsMock.mockReturnValue({
      role: "viewer",
      hasPermission: (p: string) => p === KartovaPermissions.CatalogRead,
      isLoading: false,
      isError: false,
    });

    render(<AppLayout />, { wrapper });

    expect(screen.getByTestId("sidebar")).toBeInTheDocument();
    expect(screen.getByTestId("topbar")).toBeInTheDocument();
    expect(screen.queryByText(/no access/i)).toBeNull();
  });

  it("renders NoAccessPage when user has zero permissions", () => {
    usePermissionsMock.mockReturnValue({
      role: null,
      hasPermission: () => false,
      isLoading: false,
      isError: false,
    });

    render(<AppLayout />, { wrapper });

    expect(screen.getByText(/no access/i)).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar")).toBeNull();
    expect(screen.queryByTestId("topbar")).toBeNull();
  });

  it("renders skeleton while permissions are loading", () => {
    usePermissionsMock.mockReturnValue({
      role: null,
      hasPermission: () => false,
      isLoading: true,
      isError: false,
    });

    render(<AppLayout />, { wrapper });

    expect(screen.getByText(/loading/i)).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar")).toBeNull();
    expect(screen.queryByText(/no access/i)).toBeNull();
  });

  it("renders error placeholder when permissions fetch errors", () => {
    usePermissionsMock.mockReturnValue({
      role: null,
      hasPermission: () => false,
      isLoading: false,
      isError: true,
    });

    render(<AppLayout />, { wrapper });

    expect(screen.getByText(/couldn.t load your permissions/i)).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar")).toBeNull();
    expect(screen.queryByTestId("topbar")).toBeNull();
    expect(screen.queryByText(/no access/i)).toBeNull();
  });
});
