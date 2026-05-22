import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { ApplicationDetailPage } from "../ApplicationDetailPage";

// Default: fully permissive — existing tests are unaffected.
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
  });
}

function harness(qc: QueryClient, initialPath: string) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/catalog/applications/:id" element={<ApplicationDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("ApplicationDetailPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    // Fully permissive default so existing tests are unaffected.
    mockPermissions(Object.values(KartovaPermissions));
  });

  it("renders application metadata when query resolves", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "00000000-0000-0000-0000-000000000001",
        tenantId: "t",
        name: "payment-gateway",
        displayName: "Payment Gateway",
        description: "Handles charges",
        ownerUserId: "u-1",
        createdAt: "2026-01-01T12:34:56Z",
        lifecycle: "active",
        sunsetDate: null,
        version: "v1",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.getByText("payment-gateway")).toBeInTheDocument();
    expect(screen.getByText("Handles charges")).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
  });

  it("calls GET with the path id from the URL", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "abc",
        tenantId: "t",
        name: "x",
        displayName: "X",
        description: "d",
        ownerUserId: "u",
        createdAt: "2026-01-01T00:00:00Z",
        lifecycle: "active",
        sunsetDate: null,
        version: "v1",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/abc"));

    await waitFor(() => {
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications/{id}",
        { params: { path: { id: "abc" } } }
      );
    });
  });

  it("renders skeletons while loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {})); // never resolves
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(harness(qc, "/catalog/applications/x"));

    expect(container.querySelectorAll('[data-testid="detail-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders error state when query errors", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 404, title: "Not Found" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/missing"));

    await waitFor(() => expect(screen.getByText(/application not found/i)).toBeInTheDocument());
  });

  it("falls back to italic 'No description' when description is empty", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "1",
        tenantId: "t",
        name: "n",
        displayName: "N",
        description: "",
        ownerUserId: "u",
        createdAt: "2026-01-01T00:00:00Z",
        lifecycle: "active",
        sunsetDate: null,
        version: "v1",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/1"));

    await waitFor(() => expect(screen.getByText(/no description/i)).toBeInTheDocument());
  });
});

// ---------------------------------------------------------------------------
// Permission gating tests (Slice 7)
// ---------------------------------------------------------------------------

const activeApp = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  name: "payment-gateway",
  displayName: "Payment Gateway",
  description: "Handles charges",
  ownerUserId: "u-1",
  createdAt: "2026-01-01T12:34:56Z",
  lifecycle: "active",
  sunsetDate: null,
  version: "v1",
};

describe("ApplicationDetailPage — Edit button gating", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("hides Edit button for Viewer (only CatalogRead)", async () => {
    mockPermissions([KartovaPermissions.CatalogRead]);

    const get = vi.fn().mockResolvedValue({ data: activeApp, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /^edit$/i })).toBeNull();
  });

  it("shows Edit button for Member (has CatalogApplicationsEditMetadata)", async () => {
    mockPermissions([
      KartovaPermissions.CatalogRead,
      KartovaPermissions.CatalogApplicationsEditMetadata,
    ]);

    const get = vi.fn().mockResolvedValue({ data: activeApp, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^edit$/i })).toBeInTheDocument(),
    );
  });
});

describe("ApplicationDetailPage — LifecycleMenu gating", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("hides LifecycleMenu when user has neither forward nor reverse lifecycle permission", async () => {
    mockPermissions([KartovaPermissions.CatalogRead]);

    const get = vi.fn().mockResolvedValue({ data: activeApp, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
  });

  it("shows LifecycleMenu when user has forward lifecycle permission", async () => {
    mockPermissions([
      KartovaPermissions.CatalogRead,
      KartovaPermissions.CatalogApplicationsLifecycleForward,
    ]);

    const get = vi.fn().mockResolvedValue({ data: activeApp, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /open lifecycle menu/i })).toBeInTheDocument(),
    );
  });
});
