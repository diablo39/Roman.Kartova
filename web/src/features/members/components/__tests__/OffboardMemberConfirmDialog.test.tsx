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

// Stub UserSearchCombobox — drives onSelect with a fixture user deterministically,
// without coupling to the debounced-search wire (mirrors AddMemberDialog.test.tsx).
const SUCCESSOR = {
  id: "22222222-3333-4444-8555-666666666666",
  displayName: "Successor User",
  email: "successor@example.com",
} as const;

// Capture the last excludeUserId prop for assertion in tests.
let capturedExcludeUserId: string | undefined;

vi.mock("@/features/users/components/UserSearchCombobox", () => ({
  UserSearchCombobox: ({
    onSelect,
    excludeUserId,
  }: {
    onSelect: (u: typeof SUCCESSOR) => void;
    excludeUserId?: string;
  }) => {
    capturedExcludeUserId = excludeUserId;
    return (
      <button type="button" data-testid="mock-successor-pick" onClick={() => onSelect(SUCCESSOR)}>
        Pick successor
      </button>
    );
  },
}));

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

  it("Remove button is disabled until a successor is selected", () => {
    setup();
    expect(screen.getByRole("button", { name: /^remove$/i })).toBeDisabled();
  });

  it("Remove button becomes enabled after selecting a successor", async () => {
    setup();
    await userEvent.click(screen.getByTestId("mock-successor-pick"));
    expect(screen.getByRole("button", { name: /^remove$/i })).not.toBeDisabled();
  });

  it("shows 'Selected:' line after a successor is picked", async () => {
    setup();
    await userEvent.click(screen.getByTestId("mock-successor-pick"));
    expect(screen.getByText(/selected:/i)).toBeInTheDocument();
    expect(
      screen.getByText(`${SUCCESSOR.displayName} (${SUCCESSOR.email})`),
    ).toBeInTheDocument();
  });

  it("clicking Remove calls mutateAsync with userId and successorUserId, toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue(undefined);
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.click(screen.getByTestId("mock-successor-pick"));
    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        userId: USER_ID,
        successorUserId: SUCCESSOR.id,
      }),
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

    await userEvent.click(screen.getByTestId("mock-successor-pick"));
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

    await userEvent.click(screen.getByTestId("mock-successor-pick"));
    await userEvent.click(screen.getByRole("button", { name: /^remove$/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Unprocessable Entity"),
    );
  });

  it("passes the target userId as excludeUserId to UserSearchCombobox so the member cannot pick themselves as successor", () => {
    capturedExcludeUserId = undefined;
    setup({ userId: USER_ID });
    expect(capturedExcludeUserId).toBe(USER_ID);
  });

  it("resets selected successor when dialog closes", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const onOpenChange = vi.fn();

    const { rerender } = render(
      <QueryClientProvider client={qc}>
        <OffboardMemberConfirmDialog
          userId={USER_ID}
          displayName={DISPLAY_NAME}
          open={true}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );

    // Pick a successor.
    await userEvent.click(screen.getByTestId("mock-successor-pick"));
    expect(screen.getByText(/selected:/i)).toBeInTheDocument();

    // Close the dialog.
    rerender(
      <QueryClientProvider client={qc}>
        <OffboardMemberConfirmDialog
          userId={USER_ID}
          displayName={DISPLAY_NAME}
          open={false}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );

    // Reopen — selected successor should be gone.
    rerender(
      <QueryClientProvider client={qc}>
        <OffboardMemberConfirmDialog
          userId={USER_ID}
          displayName={DISPLAY_NAME}
          open={true}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );

    expect(screen.queryByText(/selected:/i)).toBeNull();
    expect(screen.getByRole("button", { name: /^remove$/i })).toBeDisabled();
  });
});
