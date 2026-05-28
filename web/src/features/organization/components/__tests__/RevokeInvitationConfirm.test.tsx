import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

vi.mock("sonner", () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

import { toast } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { RevokeInvitationConfirm } from "../RevokeInvitationConfirm";
import type { InvitationResponse } from "@/features/organization/api/invitations";

const INVITATION: InvitationResponse = {
  id: "inv-1",
  email: "alice@example.com",
  role: "Member",
  invitedAt: "2026-01-01T00:00:00Z",
  expiresAt: "2026-01-08T00:00:00Z",
  status: "Pending",
  invitedByUserId: "u-1",
  acceptedAt: null,
  revokedAt: null,
};

function harness({
  post,
  invitation = INVITATION,
  open = true,
  onOpenChange = vi.fn(),
}: {
  post: ReturnType<typeof vi.fn>;
  invitation?: InvitationResponse | null;
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
      <RevokeInvitationConfirm
        invitation={invitation}
        open={open}
        onOpenChange={onOpenChange}
      />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("RevokeInvitationConfirm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the empty dialog (no content) when invitation is null", () => {
    harness({ post: vi.fn(), invitation: null });
    // The dialog overlay still renders (parent controls `open`) but the body
    // is empty because we render nothing inside `<Dialog>` when invitation is
    // null. No "Revoke invitation?" heading.
    expect(screen.queryByText(/revoke invitation\?/i)).toBeNull();
  });

  it("happy path: clicks Revoke, calls POST and closes", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: undefined,
      response: { status: 204 },
    });
    const onOpenChange = vi.fn();
    harness({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^revoke$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/organizations/invitations/{id}/revoke",
      { params: { path: { id: INVITATION.id } } },
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(toast.success).toHaveBeenCalledWith("Invitation revoked");
  });

  it("on 404 treats as success (toast success + close)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Not Found" },
      response: { status: 404 },
    });
    const onOpenChange = vi.fn();
    harness({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^revoke$/i }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(toast.success).toHaveBeenCalledWith("Invitation was already gone");
  });

  it("on 409 toasts error and closes (state no longer matches)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Conflict" },
      response: { status: 409 },
    });
    const onOpenChange = vi.fn();
    harness({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^revoke$/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Invitation is no longer pending"),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("on 502 toasts error and keeps the dialog open for retry", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Bad Gateway" },
      response: { status: 502 },
    });
    const onOpenChange = vi.fn();
    harness({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^revoke$/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "Identity provider unavailable — try again shortly",
      ),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
