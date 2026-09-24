import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";

const useEnvironmentMock = vi.fn();
// useEditEnvironment/useDeleteEnvironment are left as the REAL hooks (importActual) so the
// click tests below exercise real click → dialog-open → real PUT/DELETE-with-If-Match wiring
// against a mocked apiClient, mirroring VmDetailPage.test.tsx's "Delete opens the confirm
// dialog and issues DELETE with If-Match" pattern — a button-visibility assertion alone
// can't catch a regression in the onClick handlers or the onDeleted → navigate wiring.
vi.mock("@/features/catalog/api/environments", async () => {
  const actual = await vi.importActual<typeof import("@/features/catalog/api/environments")>(
    "@/features/catalog/api/environments",
  );
  return { ...actual, useEnvironment: (...a: unknown[]) => useEnvironmentMock(...a) };
});

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
      <Toaster />
      <MemoryRouter initialEntries={[`/catalog/environments/${ENV_ID}`]}>
        <Routes>
          <Route path="/catalog/environments/:id" element={<EnvironmentDetailPage />} />
          <Route path="/catalog/environments" element={<div>Environments List</div>} />
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

  it("Edit opens the edit dialog pre-filled from the loaded environment", async () => {
    usePermissionsMock.mockReturnValue({
      role: "Member",
      hasPermission: (p: string) => p === "catalog.environments.edit",
      isLoading: false,
      isError: false,
    });
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Edit" }));

    expect(screen.getByRole("dialog", { name: /edit environment/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/display name/i)).toHaveValue("Prod Environment");
  });

  it("Delete opens the confirm dialog and issues DELETE with If-Match, then navigates to the list", async () => {
    usePermissionsMock.mockReturnValue({
      role: "OrgAdmin",
      hasPermission: (p: string) => p === "catalog.environments.delete",
      isLoading: false,
      isError: false,
    });
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    const del = vi.fn().mockResolvedValue({ data: undefined, error: undefined, response: { status: 204 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn(),
      POST: vi.fn(),
      PUT: vi.fn(),
      DELETE: del,
    } as never);

    renderPage();

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(screen.getByRole("dialog", { name: /delete environment/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /delete environment/i }));

    await waitFor(() => expect(del).toHaveBeenCalled());
    expect(del).toHaveBeenCalledWith(
      "/api/v1/catalog/environments/{id}",
      {
        params: { path: { id: ENV_ID } },
        headers: { "If-Match": '"v1"' },
      },
    );
    expect(await screen.findByText("Environments List")).toBeInTheDocument();
  });
});
