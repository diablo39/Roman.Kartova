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

// Mock the offboard mutation hook.
const mutateAsync = vi.fn();
vi.mock("@/features/members/api/members", () => ({
  useOffboardMember: () => ({ mutateAsync, isPending: false }),
}));

import { OffboardMemberConfirmDialog } from "../OffboardMemberConfirmDialog";

const USER_ID = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
const DISPLAY_NAME = "Alice Example";

function setup({
  userId = USER_ID,
  displayName = DISPLAY_NAME,
  open = true,
  onOpenChange = vi.fn(),
}: {
  userId?: string;
  displayName?: string;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
} = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <OffboardMemberConfirmDialog
        userId={userId}
        displayName={displayName}
        open={open}
        onOpenChange={onOpenChange}
      />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("OffboardMemberConfirmDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing when closed", () => {
    setup({ open: false });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("renders the dialog with displayName in the warning when open", () => {
    setup();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/alice example/i)).toBeInTheDocument();
  });

  it("Remove button is enabled without any selection required", () => {
    setup();
    expect(screen.getByRole("button", { name: /^remove$/i })).not.toBeDisabled();
  });

  it("shows 'created by' attribution copy in the warning text", () => {
    setup();
    expect(screen.getByText(/created by/i)).toBeInTheDocument();
  });

  it("clicking Remove calls mutateAsync with userId only, toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue(undefined);
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({ userId: USER_ID }),
    );
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Member removed"));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 409 rejected mutateAsync with ProblemDetails.detail shows toast.error with that detail", async () => {
    const problem = {
      detail: "The organization must retain at least one OrgAdmin.",
      title: "Conflict",
      status: 409,
    };
    mutateAsync.mockRejectedValue(problem);
    setup();

    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "The organization must retain at least one OrgAdmin.",
      ),
    );
  });

  it("on rejected mutateAsync with only title falls back to title", async () => {
    const problem = { title: "Unprocessable Entity", status: 422 };
    mutateAsync.mockRejectedValue(problem);
    setup();

    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Unprocessable Entity"),
    );
  });
});
