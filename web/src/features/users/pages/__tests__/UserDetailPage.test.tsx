import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import { UserDetailPage } from "../UserDetailPage";

// Mock the underlying hooks rather than the API client — this keeps the test
// focused on the page's branching logic (loading / 404 / error / populated)
// without leaking openapi-fetch internals into every assertion.
const useUserMock = vi.fn();
const useApplicationsListMock = vi.fn();
const usePermissionsMock = vi.fn();

vi.mock("@/features/users/api/users", () => ({
  useUser: (...args: unknown[]) => useUserMock(...args),
}));

vi.mock("@/features/catalog/api/applications", () => ({
  useApplicationsList: (...args: unknown[]) => useApplicationsListMock(...args),
}));

vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

const USER_ID = "00000000-0000-0000-0000-000000000001";

const baseUser = {
  id: USER_ID,
  email: "alice@example.com",
  displayName: "Alice",
  givenName: "Alice",
  familyName: "Doe",
  teams: [
    { teamId: "t-1", teamName: "Platform", role: "Admin" },
    { teamId: "t-2", teamName: "Frontend", role: "Member" },
  ],
  createdAt: "2026-01-01T00:00:00Z",
  lastSeenAt: "2026-05-01T00:00:00Z",
};

function harness(qc?: QueryClient) {
  const client = qc ?? new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/users/${USER_ID}`]}>
        <Routes>
          <Route path="/users/:id" element={<UserDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function permissions(canRead = true) {
  usePermissionsMock.mockReturnValue({
    role: "OrgAdmin",
    hasPermission: () => canRead,
    isLoading: false,
    teamIds: [],
    teamAdminTeamIds: [],
  });
}

function userQuery(state: Partial<{ isLoading: boolean; isError: boolean; data: unknown; error: unknown }>) {
  useUserMock.mockReturnValue({
    isLoading: false,
    isError: false,
    data: undefined,
    error: null,
    refetch: vi.fn(),
    ...state,
  });
}

function appsQuery(
  state: Partial<{
    isLoading: boolean;
    isError: boolean;
    items: unknown[];
    error: unknown;
    refetch: ReturnType<typeof vi.fn>;
  }>,
) {
  useApplicationsListMock.mockReturnValue({
    isLoading: false,
    isError: false,
    items: [],
    error: null,
    isFetching: false,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    reset: vi.fn(),
    refetch: vi.fn(),
    ...state,
  });
}

describe("UserDetailPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the 403 placeholder when the caller lacks OrgUsersRead", () => {
    permissions(false);
    userQuery({});
    appsQuery({});
    render(harness());

    expect(screen.getByText("Not authorized")).toBeInTheDocument();
    expect(useUserMock).toHaveBeenCalledWith(null);
  });

  it("renders the loading state while useUser is fetching", () => {
    permissions(true);
    userQuery({ isLoading: true });
    appsQuery({});
    render(harness());

    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it("renders 'User not found' on a 404 from useUser", () => {
    permissions(true);
    userQuery({ isError: true, error: { __status: 404 } });
    appsQuery({});
    render(harness());

    expect(screen.getByText("User not found")).toBeInTheDocument();
  });

  it("renders the generic error card on non-404 errors", () => {
    permissions(true);
    userQuery({ isError: true, error: { __status: 500 } });
    appsQuery({});
    render(harness());

    expect(screen.getByText(/failed to load user/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });

  it("renders all three cards populated when user + apps both succeed", async () => {
    permissions(true);
    userQuery({ data: baseUser });
    appsQuery({
      items: [
        { id: "a-1", displayName: "Billing API", lifecycle: "Active" },
        { id: "a-2", displayName: "Reports", lifecycle: "Deprecated" },
      ],
    });
    render(harness());

    // Profile card heading is the canonical "Alice" — `getAllByText` proves
    // the display name appears in more than one place (heading + dd row).
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { level: 2, name: "Alice" }),
      ).toBeInTheDocument(),
    );
    expect(screen.getAllByText("Alice").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("alice@example.com").length).toBeGreaterThanOrEqual(1);

    // Teams card
    expect(screen.getByRole("link", { name: "Platform" })).toHaveAttribute("href", "/teams/t-1");
    expect(screen.getByRole("link", { name: "Frontend" })).toHaveAttribute("href", "/teams/t-2");

    // Apps card
    expect(screen.getByRole("link", { name: "Billing API" })).toHaveAttribute(
      "href",
      "/catalog/applications/a-1",
    );
    expect(screen.getByRole("link", { name: "Reports" })).toBeInTheDocument();
  });

  it("shows the empty teams message when user has no team memberships", () => {
    permissions(true);
    userQuery({ data: { ...baseUser, teams: [] } });
    appsQuery({ items: [{ id: "a-1", displayName: "Billing API", lifecycle: "Active" }] });
    render(harness());

    expect(screen.getByText(/not on any teams/i)).toBeInTheDocument();
  });

  it("shows the empty created-apps message when the apps list is empty", () => {
    permissions(true);
    userQuery({ data: baseUser });
    appsQuery({ items: [] });
    render(harness());

    expect(screen.getByText(/has not created any applications/i)).toBeInTheDocument();
  });

  it("keeps the user card visible when the apps list errors (independent fetches)", () => {
    permissions(true);
    userQuery({ data: baseUser });
    appsQuery({ isError: true, error: new Error("apps boom") });
    render(harness());

    // User profile card heading still rendered.
    expect(
      screen.getByRole("heading", { level: 2, name: "Alice" }),
    ).toBeInTheDocument();
    expect(screen.getAllByText("alice@example.com").length).toBeGreaterThanOrEqual(1);
    // Apps card shows its own error state.
    expect(screen.getByText(/failed to load applications/i)).toBeInTheDocument();
  });

  it("'Try again' on the apps error invokes useApplicationsList.refetch (regression: reset is a no-op for first-page error)", () => {
    permissions(true);
    userQuery({ data: baseUser });
    const refetch = vi.fn();
    appsQuery({ isError: true, error: new Error("apps boom"), refetch });
    render(harness());

    // The apps card surfaces its own retry button — distinct from the user
    // card's "Try again". Scope the button query to the apps error region.
    const appsError = screen.getByText(/failed to load applications/i).parentElement!;
    const button = appsError.querySelector("button")!;
    expect(button).toHaveTextContent(/try again/i);
    fireEvent.click(button);

    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it("threads createdByUserId into useApplicationsList for the created-apps query", () => {
    permissions(true);
    userQuery({ data: baseUser });
    appsQuery({});
    render(harness());

    expect(useApplicationsListMock).toHaveBeenCalledWith(
      expect.objectContaining({ createdByUserId: USER_ID }),
    );
  });
});
