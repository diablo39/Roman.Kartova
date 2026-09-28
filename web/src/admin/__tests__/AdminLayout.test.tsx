import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const signoutRedirect = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isAuthenticated: true, isLoading: false, signoutRedirect }),
}));

import { AdminLayout } from "../layout/AdminLayout";
import { AdminLandingPage } from "../pages/AdminLandingPage";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
const operator = { userId: "3f0c1a8e-0000-4000-8000-000000000001", email: "olga@ops", displayName: "Olga Operator" };

// useAdminSession sets its own `retry` (one retry for non-401/403 after ~1 s), which overrides client
// defaults — so error cases stub fetch persistently (mockImplementation → a fresh Response per call; a
// Response body can be read only once) and wait up to 5 s.
const WAIT = { timeout: 5000 };

function renderLayout() {
  const qc = new QueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route element={<AdminLayout />}>
            <Route index element={<AdminLandingPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

let fetchSpy: MockInstance<typeof fetch>;
beforeEach(() => {
  signoutRedirect.mockClear();
  fetchSpy = vi.spyOn(globalThis, "fetch");
});
afterEach(() => vi.restoreAllMocks());

describe("AdminLayout", () => {
  it("shows a loading state while the session check is in flight", () => {
    fetchSpy.mockReturnValue(new Promise(() => {}));
    renderLayout();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("200 → master shell with admin nav, badge, and the landing page", async () => {
    fetchSpy.mockImplementation(async () => json(200, operator));
    renderLayout();

    expect(await screen.findByRole("heading", { name: "Platform Admin" })).toBeInTheDocument();
    expect(screen.getByText("Signed in as Olga Operator · olga@ops")).toBeInTheDocument();
    expect(screen.getAllByText("Platform Admin").length).toBeGreaterThanOrEqual(2); // badge + h1
    expect(screen.getByRole("link", { name: "Overview" })).toHaveAttribute("href", "/");
    expect(screen.getByText("Organizations").closest("[data-disabled='true']")).not.toBeNull();
    expect(screen.queryByRole("link", { name: /organizations/i })).toBeNull();
  });

  it("200 with empty displayName/email → dashes, not blanks", async () => {
    fetchSpy.mockImplementation(async () => json(200, { ...operator, email: "", displayName: " " }));
    renderLayout();
    expect(await screen.findByText("Signed in as — · —")).toBeInTheDocument();
  });

  it("403 → No access page without the shell, and Sign out works", async () => {
    fetchSpy.mockImplementation(async () => new Response(null, { status: 403 }));
    renderLayout();

    expect(await screen.findByRole("heading", { name: "No access" })).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(1); // 403 is terminal — never retried
    expect(screen.getByText("This account is not a platform operator.")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Overview" })).toBeNull();

    await userEvent.setup().click(screen.getByRole("button", { name: "Sign out" }));
    expect(signoutRedirect).toHaveBeenCalledTimes(1);
  });

  it("401 → signing-in state, never No access", async () => {
    fetchSpy.mockImplementation(async () => json(401, { title: "Unauthorized", status: 401 }));
    renderLayout();

    expect(await screen.findByText("Signing in…")).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(1); // 401 is terminal — never retried
    expect(screen.queryByRole("heading", { name: "No access" })).toBeNull();
  });

  it("500 → retry panel (never No access), and Retry re-checks", async () => {
    fetchSpy.mockImplementation(async () => json(500, { title: "boom", status: 500 }));
    renderLayout();

    expect(await screen.findByRole("heading", { name: "Couldn't verify admin access" }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "No access" })).toBeNull();

    fetchSpy.mockImplementation(async () => json(200, operator));
    await userEvent.setup().click(screen.getByRole("button", { name: "Retry" }));
    await waitFor(() => expect(screen.getByRole("link", { name: "Overview" })).toBeInTheDocument(), WAIT);
  });

  it("network failure (fetch rejects) → retry panel, never No access", async () => {
    fetchSpy.mockImplementation(async () => { throw new TypeError("Failed to fetch"); });
    renderLayout();

    expect(await screen.findByRole("heading", { name: "Couldn't verify admin access" }, WAIT)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "No access" })).toBeNull();
  });
});
