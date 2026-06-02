import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";

// ---------- Mocks (all hoisted by vi.mock) ----------
const useInvitationsListMock = vi.fn();
vi.mock("@/features/organization/api/invitations", async () => {
  const actual = await vi.importActual<
    typeof import("@/features/organization/api/invitations")
  >("@/features/organization/api/invitations");
  return {
    ...actual,
    useInvitationsList: (...args: unknown[]) => useInvitationsListMock(...args),
  };
});

const useUserMock = vi.fn();
vi.mock("@/features/users/api/users", async () => {
  const actual = await vi.importActual<
    typeof import("@/features/users/api/users")
  >("@/features/users/api/users");
  return {
    ...actual,
    useUser: (...args: unknown[]) => useUserMock(...args),
  };
});

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

// The dialogs are not under test here; stub them so the page renders without
// dragging in their full DOM trees (modals + portals).
vi.mock("@/features/organization/components/InviteUserDialog", () => ({
  InviteUserDialog: () => <div data-testid="invite-user-dialog-stub" />,
}));
vi.mock("@/features/organization/components/RevokeInvitationConfirm", () => ({
  RevokeInvitationConfirm: ({ invitation }: { invitation: unknown }) => (
    <div data-testid="revoke-confirm-stub" data-invitation={JSON.stringify(invitation)} />
  ),
}));

import { InvitationsPage } from "../InvitationsPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

import type { InvitationResponse } from "@/features/organization/api/invitations";

const INVITATION_PENDING: InvitationResponse = {
  id: "inv-1",
  email: "alice@example.com",
  role: "Member",
  invitedAt: "2026-01-01T00:00:00Z",
  expiresAt: "2099-01-08T00:00:00Z",
  status: "Pending",
  invitedByUserId: "u-1",
  acceptedAt: null,
  revokedAt: null,
};

const INVITATION_ACCEPTED: InvitationResponse = {
  ...INVITATION_PENDING,
  id: "inv-2",
  email: "bob@example.com",
  status: "Accepted",
  acceptedAt: "2026-01-02T00:00:00Z",
};

function listShape(items: InvitationResponse[]) {
  return {
    items,
    isLoading: false,
    isFetching: false,
    isError: false,
    error: null,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    reset: vi.fn(),
  };
}

function mockPermissions(perms: readonly string[]) {
  usePermissionsMock.mockReturnValue({
    role: "test",
    hasPermission: (p: string) => perms.includes(p),
    isLoading: false,
    isError: false,
    teamIds: [],
    teamAdminTeamIds: [],
  });
}

function harness(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

function newQc(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

describe("InvitationsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useUserMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: { id: "u-1", displayName: "Inviter Joe", email: "joe@example.com" },
    });
    mockPermissions(Object.values(KartovaPermissions));
    useInvitationsListMock.mockReturnValue(listShape([]));
  });

  it("renders a 403 placeholder when caller lacks OrgInvitationsRead", () => {
    mockPermissions([]); // no perms
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByText(/not authorized/i)).toBeInTheDocument();
    expect(
      screen.getByText(/don.?t have permission to view invitations/i),
    ).toBeInTheDocument();
  });

  it("renders the loading card while the list is loading", () => {
    useInvitationsListMock.mockReturnValue({
      ...listShape([]),
      isLoading: true,
    });
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByText(/^Loading…$/)).toBeInTheDocument();
  });

  it("renders the error card when the list query errors", () => {
    useInvitationsListMock.mockReturnValue({
      ...listShape([]),
      isError: true,
      error: new Error("boom"),
    });
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByText(/failed to load invitations/i)).toBeInTheDocument();
  });

  it("renders the empty state when the list returns 0 items", () => {
    useInvitationsListMock.mockReturnValue(listShape([]));
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByText(/no invitations/i)).toBeInTheDocument();
  });

  it("renders one row per invitation with email + role + status badge", () => {
    useInvitationsListMock.mockReturnValue(
      listShape([INVITATION_PENDING, INVITATION_ACCEPTED]),
    );
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
    // "Pending" appears twice — as the active tab label AND the badge for the
    // pending row. Require BOTH occurrences so a regression that drops the
    // status badge (leaving only the tab label) still fails the test.
    expect(screen.getAllByText("Pending").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("Accepted")).toBeInTheDocument();
  });

  it("Pending tab (default) passes status='Pending' to the hook", () => {
    useInvitationsListMock.mockReturnValue(listShape([]));
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(useInvitationsListMock).toHaveBeenCalledWith(
      expect.objectContaining({ status: "Pending" }),
    );
  });

  it("switching to the All tab passes status='all' to the hook", async () => {
    // Spec §6.7: a missing `status` query parameter is server-side
    // shorthand for the default Pending filter. To list every lifecycle
    // state the All tab MUST pass the explicit "all" sentinel — leaving
    // it undefined would silently land on Pending again.
    useInvitationsListMock.mockReturnValue(listShape([]));
    render(<InvitationsPage />, { wrapper: harness(newQc()) });

    // The default tab is "Pending" — verified above. Click "All".
    await userEvent.click(screen.getByRole("button", { name: /^All$/ }));

    await waitFor(() =>
      expect(useInvitationsListMock).toHaveBeenLastCalledWith(
        expect.objectContaining({ status: "all" }),
      ),
    );
  });

  it("Invite user button is gated on OrgInvitationsCreate", () => {
    // Without the permission → button hidden.
    mockPermissions([KartovaPermissions.OrgInvitationsRead]);
    const { unmount } = render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.queryByRole("button", { name: /invite user/i })).toBeNull();
    unmount();

    // With the permission → button visible.
    mockPermissions([
      KartovaPermissions.OrgInvitationsRead,
      KartovaPermissions.OrgInvitationsCreate,
    ]);
    render(<InvitationsPage />, { wrapper: harness(newQc()) });
    expect(screen.getByRole("button", { name: /invite user/i })).toBeInTheDocument();
  });

  it("Revoke button is only shown on Pending rows when OrgInvitationsRevoke is granted", () => {
    mockPermissions([
      KartovaPermissions.OrgInvitationsRead,
      KartovaPermissions.OrgInvitationsRevoke,
    ]);
    useInvitationsListMock.mockReturnValue(
      listShape([INVITATION_PENDING, INVITATION_ACCEPTED]),
    );
    render(<InvitationsPage />, { wrapper: harness(newQc()) });

    // Exactly one Revoke button — the one on the Pending row.
    const revokeButtons = screen.getAllByRole("button", { name: /^revoke$/i });
    expect(revokeButtons).toHaveLength(1);
  });
});
