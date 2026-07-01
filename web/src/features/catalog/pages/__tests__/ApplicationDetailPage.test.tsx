import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { ApplicationDetailPage } from "../ApplicationDetailPage";

// Stub relationships so RelationshipsSection renders without real API calls.
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: () => ({ items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() }),
  useDeleteRelationship: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

// Default: fully permissive — existing tests are unaffected.
const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

import { KartovaPermissions } from "@/shared/auth/permissions";

function mockPermissions(
  perms: string[],
  overrides?: { role?: string; teamIds?: string[] }
) {
  usePermissionsMock.mockReturnValue({
    role: overrides?.role ?? "test",
    hasPermission: (p: string) => perms.includes(p),
    isLoading: false,
    teamIds: overrides?.teamIds ?? [],
    teamAdminTeamIds: [],
    isError: false,
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
        displayName: "Payment Gateway",
        description: "Handles charges",
        createdByUserId: "00000000-0000-0000-0000-0000000000u1",
        createdBy: {
          id: "00000000-0000-0000-0000-0000000000u1",
          displayName: "Alice Owner",
          email: "alice@example.com",
        },
        createdAt: "2026-01-01T12:34:56Z",
        lifecycle: "active",
        sunsetDate: null,
        teamId: null,
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
    expect(screen.getByText("Handles charges")).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
  });

  it("renders CreatedByLink to /users/{id} when createdBy is present (slice-10 ownership realignment)", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "00000000-0000-0000-0000-000000000001",
        tenantId: "t",
        displayName: "Payment Gateway",
        description: "Handles charges",
        createdByUserId: "00000000-0000-0000-0000-0000000000u1",
        createdBy: {
          id: "00000000-0000-0000-0000-0000000000u1",
          displayName: "Alice Owner",
          email: "alice@example.com",
        },
        createdAt: "2026-01-01T12:34:56Z",
        lifecycle: "active",
        sunsetDate: null,
        teamId: null,
        version: "v1",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    const ownerLink = await screen.findByRole("link", { name: /alice owner/i });
    expect(ownerLink).toHaveAttribute("href", "/users/00000000-0000-0000-0000-0000000000u1");
  });

  it("renders 'Unknown user' fallback when createdBy is null (offboarded creator, slice-10)", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "00000000-0000-0000-0000-000000000001",
        tenantId: "t",
        displayName: "Orphaned App",
        description: "Creator since offboarded",
        createdByUserId: "00000000-0000-0000-0000-0000000000u1",
        createdBy: null,
        createdAt: "2026-01-01T12:34:56Z",
        lifecycle: "active",
        sunsetDate: null,
        teamId: null,
        version: "v1",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Orphaned App")).toBeInTheDocument());
    expect(screen.getByText(/unknown user/i)).toBeInTheDocument();
  });

  it("calls GET with the path id from the URL", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "abc",
        tenantId: "t",
        displayName: "X",
        description: "d",
        createdByUserId: "u",
        createdAt: "2026-01-01T00:00:00Z",
        lifecycle: "active",
        sunsetDate: null,
        teamId: null,
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
        displayName: "N",
        description: "",
        createdByUserId: "u",
        createdAt: "2026-01-01T00:00:00Z",
        lifecycle: "active",
        sunsetDate: null,
        teamId: null,
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
  displayName: "Payment Gateway",
  description: "Handles charges",
  createdByUserId: "u-1",
  createdAt: "2026-01-01T12:34:56Z",
  lifecycle: "active",
  sunsetDate: null,
  teamId: null,
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

// ---------------------------------------------------------------------------
// Successor link + manage-button gating (ADR-0110 §5.3, Task C6)
// ---------------------------------------------------------------------------

const deprecatedAppWithSuccessor = {
  ...activeApp,
  lifecycle: "deprecated",
  teamId: "team-1",
  successorApplicationId: "00000000-0000-0000-0000-000000000def",
  successorDisplayName: "Payments v2",
};

const deprecatedAppNoSuccessor = {
  ...activeApp,
  lifecycle: "deprecated",
  teamId: "team-1",
  successorApplicationId: null,
  successorDisplayName: null,
};

describe("ApplicationDetailPage — successor link + manage button gating", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the successor link when successorApplicationId is present", async () => {
    mockPermissions([KartovaPermissions.CatalogRead]);

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppWithSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    const link = await screen.findByRole("link", { name: /Payments v2/i });
    expect(link).toHaveAttribute(
      "href",
      "/catalog/applications/00000000-0000-0000-0000-000000000def"
    );
  });

  it("hides the successor section when no successor is set and the user cannot manage it", async () => {
    mockPermissions([KartovaPermissions.CatalogRead]);

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppNoSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByText(/successor/i)).toBeNull();
  });

  it("shows the 'Change successor' button when Deprecated + OrgAdmin", async () => {
    mockPermissions(
      [KartovaPermissions.CatalogApplicationsLifecycleForward],
      { role: "OrgAdmin", teamIds: [] }
    );

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppWithSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /change successor/i })).toBeInTheDocument()
    );
  });

  it("shows the 'Set successor' button when Deprecated + team member with forward permission", async () => {
    mockPermissions(
      [KartovaPermissions.CatalogApplicationsLifecycleForward],
      { role: "Member", teamIds: ["team-1"] }
    );

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppNoSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /set successor/i })).toBeInTheDocument()
    );
  });

  it("hides the manage button when Deprecated but the user lacks forward permission", async () => {
    mockPermissions([KartovaPermissions.CatalogRead], { role: "OrgAdmin", teamIds: [] });

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppWithSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /(change|set) successor/i })).toBeNull();
  });

  it("hides the manage button when not Deprecated, even with permission + OrgAdmin", async () => {
    mockPermissions(
      [KartovaPermissions.CatalogApplicationsLifecycleForward],
      { role: "OrgAdmin", teamIds: [] }
    );

    const get = vi.fn().mockResolvedValue({ data: activeApp, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /(change|set) successor/i })).toBeNull();
  });

  it("hides the manage button when Deprecated + forward permission but not OrgAdmin nor a team member", async () => {
    mockPermissions(
      [KartovaPermissions.CatalogApplicationsLifecycleForward],
      { role: "Member", teamIds: ["some-other-team"] }
    );

    const get = vi.fn().mockResolvedValue({ data: deprecatedAppWithSuccessor, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/applications/00000000-0000-0000-0000-000000000001"));

    await waitFor(() => expect(screen.getByText("Payment Gateway")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /(change|set) successor/i })).toBeNull();
  });
});
