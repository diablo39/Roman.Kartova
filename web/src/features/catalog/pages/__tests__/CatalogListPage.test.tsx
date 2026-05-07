import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { CatalogListPage } from "../CatalogListPage";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: {
      access_token: "t",
      profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" },
    },
  }),
}));

function harness(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

/** Returns a cursor page envelope matching CursorPageOfApplicationResponse. */
function pageOf<T>(items: T[]) {
  return { items, nextCursor: null, prevCursor: null };
}

describe("CatalogListPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders heading and Register Application button", () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    expect(screen.getByRole("heading", { name: /catalog/i, level: 2 })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /register application/i })).toBeInTheDocument();
  });

  it("renders empty state when API returns no rows", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText(/no applications yet/i)).toBeInTheDocument());
  });

  it("renders rows when API returns applications", async () => {
    const get = vi.fn().mockResolvedValue({
      data: pageOf([
        {
          id: "00000000-0000-0000-0000-000000000001",
          tenantId: "t",
          name: "n1",
          displayName: "App One",
          description: "d",
          ownerUserId: "u",
          createdAt: "2026-01-01T00:00:00Z",
          lifecycle: "active",
          sunsetDate: null,
          version: "v1",
        },
      ]),
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText("App One")).toBeInTheDocument());
  });

  it("renders an inline error card when the list query errors", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "boom" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText(/failed to load applications/i)).toBeInTheDocument());
  });

  it("toggles dialog open state when Register Application is clicked", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    const btn = screen.getByRole("button", { name: /register application/i });
    await userEvent.click(btn);
    // The dialog itself wires up in Task 18; for now the button must at least be clickable
    // and not throw.
    expect(btn).toBeInTheDocument();
  });

  it("opens the Register Application dialog on button click", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get,
      POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await userEvent.click(screen.getByRole("button", { name: /register application/i }));
    expect(await screen.findByRole("dialog", { name: /register application/i })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Show decommissioned checkbox — URL round-trip tests (Slice 6)
// Uses Routes + Route so useSearchParams can update the URL in MemoryRouter.
// ---------------------------------------------------------------------------

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="probe">{loc.search}</div>;
}

function harnessWithRoutes(qc: QueryClient, initialEntries: string[] = ["/"]) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={initialEntries}>
        <Routes>
          <Route path="/" element={<><CatalogListPage /><LocationProbe /></>} />
        </Routes>
        {children}
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("CatalogListPage — Show decommissioned checkbox", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("is unchecked by default and URL has no includeDecommissioned param", () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc) });

    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    expect(checkbox).not.toBeChecked();
    expect(screen.getByTestId("probe").textContent).toBe("");
  });

  it("hydrates to checked when URL has ?includeDecommissioned=true", () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc, ["/?includeDecommissioned=true"]) });

    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    expect(checkbox).toBeChecked();
  });

  it("toggling the checkbox writes the URL param to true", async () => {
    const user = userEvent.setup();
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc) });

    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    await user.click(checkbox);
    expect(screen.getByTestId("probe").textContent).toContain("includeDecommissioned=true");
  });

  it("toggling off removes the URL param entirely (no =false clutter)", async () => {
    const user = userEvent.setup();
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc, ["/?includeDecommissioned=true"]) });

    const checkbox = screen.getByRole("checkbox", { name: /show decommissioned/i });
    await user.click(checkbox);
    expect(screen.getByTestId("probe").textContent).not.toContain("includeDecommissioned");
  });
});
