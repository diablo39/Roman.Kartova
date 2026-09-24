import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const useEnvironmentMock = vi.fn();
vi.mock("@/features/catalog/api/environments", () => ({
  useEnvironment: (...a: unknown[]) => useEnvironmentMock(...a),
  useEditEnvironment: vi.fn(() => ({ mutateAsync: vi.fn(), isPending: false })),
  useDeleteEnvironment: vi.fn(() => ({ mutateAsync: vi.fn(), isPending: false })),
}));

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

import { EnvironmentDetailPage } from "../EnvironmentDetailPage";

const ENV_ID = "00000000-0000-0000-0000-000000000abc";

// displayName is deliberately NOT "Production" — the type badge renders the Title-cased
// type label ("Production"), which would otherwise collide with a getByText lookup on the
// heading's own displayName.
function baseEnv(overrides: Record<string, unknown> = {}) {
  return {
    id: ENV_ID,
    tenantId: "t",
    displayName: "Prod Environment",
    description: "Primary production environment",
    type: "production",
    region: "eu-west-1",
    resourceDetails: { clusterSize: "5" },
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    version: "v1",
    ...overrides,
  };
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/catalog/environments/${ENV_ID}`]}>
        <Routes>
          <Route path="/catalog/environments/:id" element={<EnvironmentDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("EnvironmentDetailPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    usePermissionsMock.mockReturnValue({
      role: "Member",
      hasPermission: () => false,
      isLoading: false,
      isError: false,
    });
  });

  it("shows a loading skeleton while the query is in flight", () => {
    useEnvironmentMock.mockReturnValue({ isLoading: true, isError: false, data: undefined });
    renderPage();
    expect(screen.getByTestId("environment-detail-skeleton")).toBeInTheDocument();
  });

  it("renders name, type, region, resource details, and created info", () => {
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    expect(screen.getByText("Prod Environment")).toBeInTheDocument();
    expect(screen.getByText("Region")).toBeInTheDocument();
    expect(screen.getByText("eu-west-1")).toBeInTheDocument();
    expect(screen.getByText("clusterSize")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText(ENV_ID)).toBeInTheDocument();
  });

  it("renders a not-found card on a real 404", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: true,
      error: { status: 404, title: "Not Found", detail: "Environment not found." },
      data: undefined,
      refetch: vi.fn(),
    });
    renderPage();

    expect(screen.getByText("Environment not found")).toBeInTheDocument();
    expect(screen.getByText("Environment not found.")).toBeInTheDocument();
    expect(screen.queryByText("Failed to load environment")).not.toBeInTheDocument();
  });

  it("renders a distinct 'Failed to load' card and logs on a non-404 error (e.g. 500)", () => {
    const consoleErrorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const error = { status: 500, title: "Internal Server Error" };
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: true,
      error,
      data: undefined,
      refetch: vi.fn(),
    });
    renderPage();

    expect(screen.getByText("Failed to load environment")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    // Must NOT be conflated with the not-found/deletion framing.
    expect(screen.queryByText("Environment not found")).not.toBeInTheDocument();
    expect(screen.queryByText(/may have been deleted/i)).not.toBeInTheDocument();
    expect(consoleErrorSpy).toHaveBeenCalledWith("EnvironmentDetailPage load error", error);
  });

  it("renders a generic no-description placeholder when description is empty", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: baseEnv({ description: "" }),
    });
    renderPage();

    expect(screen.getByText("No description")).toBeInTheDocument();
  });

  it("renders em-dash placeholders when region is null and no resource details", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: baseEnv({ region: null, resourceDetails: {} }),
    });
    renderPage();

    expect(screen.getByText("No resource details recorded")).toBeInTheDocument();
  });

  it("hides Edit/Delete buttons without the corresponding permissions", () => {
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    expect(screen.queryByRole("button", { name: "Edit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("shows Edit when CatalogEnvironmentsEdit is granted", () => {
    usePermissionsMock.mockReturnValue({
      role: "Member",
      hasPermission: (p: string) => p === "catalog.environments.edit",
      isLoading: false,
      isError: false,
    });
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    expect(screen.getByRole("button", { name: "Edit" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("shows Delete when CatalogEnvironmentsDelete is granted (OrgAdmin)", () => {
    usePermissionsMock.mockReturnValue({
      role: "OrgAdmin",
      hasPermission: (p: string) => p === "catalog.environments.delete",
      isLoading: false,
      isError: false,
    });
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit" })).not.toBeInTheDocument();
  });
});
