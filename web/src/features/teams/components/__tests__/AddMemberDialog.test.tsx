import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";

// Mock UserSearchCombobox so we don't need to drive the debounced typeahead +
// search wire here. The mock exposes a single button that fires onSelect with
// a fixture user — this exercises the Controller wiring (userId field set,
// "Selected:" line rendered) without coupling the dialog test to the
// combobox's internal behaviour (which is covered by its own dedicated suite).
const PICKED_USER = {
  id: "11111111-2222-4333-8444-555555555555",
  displayName: "Test User",
  email: "test@example.com",
} as const;
vi.mock("@/features/users/components/UserSearchCombobox", () => ({
  UserSearchCombobox: ({ onSelect }: { onSelect: (u: typeof PICKED_USER) => void }) => (
    <button type="button" data-testid="mock-user-pick" onClick={() => onSelect(PICKED_USER)}>
      Pick user
    </button>
  ),
}));

import { AddMemberDialog } from "../AddMemberDialog";

function setup({
  post,
  open = true,
  onOpenChange = vi.fn(),
}: {
  post: ReturnType<typeof vi.fn>;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: post,
    PUT: vi.fn(),
    DELETE: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <AddMemberDialog teamId="team-1" open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("AddMemberDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("blocks submit when no user has been picked (zod userId.uuid validation)", async () => {
    const post = vi.fn();
    setup({ post });

    await userEvent.click(screen.getByRole("button", { name: /^add member$/i }));
    // userId === "" doesn't pass z.string().uuid(); zod's default message reads
    // "Must be a valid UUID" per the addTeamMember schema. The mutation
    // never fires.
    await waitFor(() => expect(post).not.toHaveBeenCalled());
  });

  it("posts userId + role to the team-members endpoint after combobox select", async () => {
    const post = vi.fn().mockResolvedValue({
      data: { userId: PICKED_USER.id, role: "Member", addedAt: "2026-01-01T00:00:00Z" },
      error: undefined,
      response: { status: 201 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    // Simulate the combobox firing its onSelect via the mocked button.
    await userEvent.click(screen.getByTestId("mock-user-pick"));
    // The dialog should reflect the pick in its "Selected:" line.
    expect(screen.getByText(/selected:/i)).toBeInTheDocument();
    expect(screen.getByText(PICKED_USER.displayName)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^add member$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/organizations/teams/{id}/members",
      expect.objectContaining({
        params: { path: { id: "team-1" } },
        body: { userId: PICKED_USER.id, role: "Member" },
      }),
    );
    await waitFor(() => expect(screen.getByText(/member added/i)).toBeInTheDocument());
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("surfaces a problem-details detail via toast on failure", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "about:blank",
        title: "Conflict",
        detail: "User is already a member",
        status: 409,
      },
      response: { status: 409 },
    });
    setup({ post });

    await userEvent.click(screen.getByTestId("mock-user-pick"));
    await userEvent.click(screen.getByRole("button", { name: /^add member$/i }));

    await waitFor(() =>
      expect(screen.getByText(/user is already a member/i)).toBeInTheDocument(),
    );
  });

  it("clears selectedUser state when the dialog closes (re-open shows no stale 'Selected:' line)", async () => {
    const post = vi.fn();
    const onOpenChange = vi.fn();
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn(),
      POST: post,
      PUT: vi.fn(),
      DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { rerender } = render(
      <QueryClientProvider client={qc}>
        <Toaster />
        <AddMemberDialog teamId="team-1" open={true} onOpenChange={onOpenChange} />
      </QueryClientProvider>,
    );

    await userEvent.click(screen.getByTestId("mock-user-pick"));
    expect(screen.getByText(/selected:/i)).toBeInTheDocument();

    // Close the dialog. The useEffect on `open` should reset the selected
    // user; re-opening with a fresh effect re-runs with no leftover state.
    rerender(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <Toaster />
        <AddMemberDialog teamId="team-1" open={false} onOpenChange={onOpenChange} />
      </QueryClientProvider>,
    );

    rerender(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <Toaster />
        <AddMemberDialog teamId="team-1" open={true} onOpenChange={onOpenChange} />
      </QueryClientProvider>,
    );

    expect(screen.queryByText(/selected:/i)).toBeNull();
  });
});
