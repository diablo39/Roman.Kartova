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

// Mock the members hook so we don't need the real API client wired up.
const mutateAsync = vi.fn();
vi.mock("@/features/members/api/members", () => ({
  useChangeMemberRole: () => ({ mutateAsync, isPending: false }),
}));

import { ChangeMemberRoleDialog } from "../ChangeMemberRoleDialog";

function setup({
  userId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
  currentRole = "Member",
  open = true,
  onOpenChange = vi.fn(),
}: {
  userId?: string;
  currentRole?: string;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
} = {}) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <ChangeMemberRoleDialog
        userId={userId}
        currentRole={currentRole}
        open={open}
        onOpenChange={onOpenChange}
      />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("ChangeMemberRoleDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing when closed", () => {
    setup({ open: false });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("renders the dialog with current role pre-selected when open", () => {
    setup({ currentRole: "Member" });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    const select = screen.getByRole("combobox");
    expect((select as HTMLSelectElement).value).toBe("Member");
  });

  it("Save button is disabled when role has not changed", () => {
    setup({ currentRole: "Member" });
    expect(screen.getByRole("button", { name: /save/i })).toBeDisabled();
  });

  it("selecting a different role enables Save, calls mutateAsync, toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue(undefined);
    const onOpenChange = vi.fn();
    setup({ currentRole: "Member", onOpenChange });

    await userEvent.selectOptions(screen.getByRole("combobox"), "OrgAdmin");
    const saveBtn = screen.getByRole("button", { name: /save/i });
    expect(saveBtn).not.toBeDisabled();

    await userEvent.click(saveBtn);

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        userId: "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
        role: "OrgAdmin",
      }),
    );
    await waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith(
        "Role updated. Takes effect on the member's next login.",
      ),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on rejected mutateAsync with ProblemDetails.detail calls toast.error with that detail", async () => {
    const problem = { detail: "Cannot demote the last OrgAdmin.", title: "Conflict" };
    mutateAsync.mockRejectedValue(problem);
    setup({ currentRole: "Member" });

    await userEvent.selectOptions(screen.getByRole("combobox"), "OrgAdmin");
    await userEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Cannot demote the last OrgAdmin."),
    );
  });

  it("on rejected mutateAsync with only ProblemDetails.title falls back to title", async () => {
    const problem = { title: "Conflict" };
    mutateAsync.mockRejectedValue(problem);
    setup({ currentRole: "Member" });

    await userEvent.selectOptions(screen.getByRole("combobox"), "OrgAdmin");
    await userEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Conflict"));
  });

  it("syncs role back to currentRole when dialog reopens with different role", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const onOpenChange = vi.fn();

    const { rerender } = render(
      <QueryClientProvider client={qc}>
        <ChangeMemberRoleDialog
          userId="u-1"
          currentRole="Member"
          open={true}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );

    // Change the select to OrgAdmin.
    await userEvent.selectOptions(screen.getByRole("combobox"), "OrgAdmin");
    expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("OrgAdmin");

    // Close and reopen with a different currentRole.
    rerender(
      <QueryClientProvider client={qc}>
        <ChangeMemberRoleDialog
          userId="u-1"
          currentRole="Viewer"
          open={false}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );
    rerender(
      <QueryClientProvider client={qc}>
        <ChangeMemberRoleDialog
          userId="u-1"
          currentRole="Viewer"
          open={true}
          onOpenChange={onOpenChange}
        />
      </QueryClientProvider>,
    );

    expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("Viewer");
  });
});
