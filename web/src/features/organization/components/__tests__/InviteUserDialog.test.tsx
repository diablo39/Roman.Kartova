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
import { InviteUserDialog } from "../InviteUserDialog";

function harness({
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
      <InviteUserDialog open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

const SUCCESS_BODY = {
  invitation: {
    id: "inv-1",
    email: "alice@example.com",
    role: "Member",
    invitedAt: "2026-01-01T00:00:00Z",
    expiresAt: "2026-01-08T00:00:00Z",
    status: "Pending",
    invitedByUserId: "u-1",
    acceptedAt: null,
    revokedAt: null,
  },
  inviteUrl: "https://kartova.io/i/abc",
};

describe("InviteUserDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing visible when closed", () => {
    harness({ post: vi.fn(), open: false });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("renders the form when open", () => {
    harness({ post: vi.fn() });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/role/i)).toBeInTheDocument();
  });

  it("submits a valid email + role to POST /invitations", async () => {
    const post = vi.fn().mockResolvedValue({
      data: SUCCESS_BODY,
      error: undefined,
      response: { status: 201 },
    });
    harness({ post });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/organizations/invitations",
      expect.objectContaining({
        body: expect.objectContaining({
          email: "alice@example.com",
          role: "Member",
        }),
      }),
    );
  });

  it("on success switches to the success view (CopyInviteLinkBox)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: SUCCESS_BODY,
      error: undefined,
      response: { status: 201 },
    });
    harness({ post });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));

    await waitFor(() =>
      expect(screen.getByText(/invitation created for alice@example\.com/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /copy invite link/i })).toBeInTheDocument();
  });

  it("'Invite another' returns to the form view and clears success state", async () => {
    const post = vi.fn().mockResolvedValue({
      data: SUCCESS_BODY,
      error: undefined,
      response: { status: 201 },
    });
    harness({ post });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /invite another/i })).toBeInTheDocument(),
    );

    await userEvent.click(screen.getByRole("button", { name: /invite another/i }));

    // Back to form view → email field is visible again.
    await waitFor(() =>
      expect(screen.getByLabelText(/email/i)).toBeInTheDocument(),
    );
    expect(screen.queryByRole("button", { name: /copy invite link/i })).toBeNull();
  });

  it("'Done' calls onOpenChange(false)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: SUCCESS_BODY,
      error: undefined,
      response: { status: 201 },
    });
    const onOpenChange = vi.fn();
    harness({ post, onOpenChange });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^done$/i })).toBeInTheDocument(),
    );

    await userEvent.click(screen.getByRole("button", { name: /^done$/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("on 409 email-already-in-tenant shows the mapped friendly toast", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/email-already-in-tenant",
        title: "Conflict",
        detail: "Already a member.",
      },
      response: { status: 409 },
    });
    harness({ post });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "That email is already a member of this tenant.",
      ),
    );
  });

  it("on 400 with field errors applies them to the form (hint text changes)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        title: "Bad Request",
        errors: { email: ["Email is not allowed here."] },
      },
      response: { status: 400 },
    });
    harness({ post });

    await userEvent.type(screen.getByLabelText(/email/i), "alice@example.com");
    await userEvent.click(screen.getByRole("button", { name: /send invite/i }));

    // The Input's `hint=` text is swapped from the default helper text to the
    // server-side validation message when fieldState.error fires.
    await waitFor(() =>
      expect(screen.getByText(/email is not allowed here\./i)).toBeInTheDocument(),
    );
  });
});
