import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from "sonner";

import { AssignSystemDialog } from "@/features/catalog/components/AssignSystemDialog";
import * as systems from "@/features/catalog/api/systems";
import * as rel from "@/features/catalog/api/relationships";

const mutateAsync = vi.fn();

function mockDeps(results: { id: string; displayName: string }[]) {
  vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: results.map((r) => ({ kind: "system", id: r.id, displayName: r.displayName })),
    isLoading: false,
  } as never);
}

function renderDialog({
  currentSystemName = null as string | null,
  onOpenChange = vi.fn(),
}: { currentSystemName?: string | null; onOpenChange?: (open: boolean) => void } = {}) {
  render(
    <AssignSystemDialog
      open
      onOpenChange={onOpenChange}
      component={{ kind: "application", id: "a1", displayName: "Checkout" }}
      currentSystemName={currentSystemName}
    />,
  );
  return { onOpenChange };
}

async function selectPayments() {
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "Pay" } });
  await waitFor(() => expect(screen.getByText("Payments")).toBeInTheDocument());
  fireEvent.click(screen.getByText("Payments"));
}

beforeEach(() => {
  vi.restoreAllMocks();
  vi.clearAllMocks();
  mutateAsync.mockReset().mockResolvedValue({ systemId: "sys1", systemDisplayName: "Payments" });
});

describe("AssignSystemDialog", () => {
  it("assigns the selected System to the component", async () => {
    mockDeps([{ id: "sys1", displayName: "Payments" }]);
    renderDialog();

    await selectPayments();

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: "sys1" }),
    );
  });

  it("clears the membership from the Remove action when already assigned", async () => {
    mockDeps([]);
    renderDialog({ currentSystemName: "Payments" });

    fireEvent.click(screen.getByRole("button", { name: /remove from system/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: null }),
    );
  });

  it("offers no Remove action when the component is unassigned", () => {
    mockDeps([]);
    renderDialog();

    expect(screen.queryByRole("button", { name: /remove from system/i })).toBeNull();
  });

  it("toasts success and closes when assign succeeds", async () => {
    mockDeps([{ id: "sys1", displayName: "Payments" }]);
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });

    await selectPayments();

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Checkout is now part of Payments."));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("toasts the ProblemDetails message and keeps the dialog open when assign fails", async () => {
    mockDeps([{ id: "sys1", displayName: "Payments" }]);
    mutateAsync.mockRejectedValueOnce({
      title: "Conflict",
      detail: "That System is archived and cannot accept new members.",
      __status: 422,
    });
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });

    await selectPayments();

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("That System is archived and cannot accept new members."),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("toasts success and closes when Remove succeeds", async () => {
    mockDeps([]);
    mutateAsync.mockResolvedValueOnce({ systemId: null, systemDisplayName: null });
    const onOpenChange = vi.fn();
    renderDialog({ currentSystemName: "Payments", onOpenChange });

    fireEvent.click(screen.getByRole("button", { name: /remove from system/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Checkout removed from its System."));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("toasts the ProblemDetails message and keeps the dialog open when Remove fails", async () => {
    mockDeps([]);
    mutateAsync.mockRejectedValueOnce({
      title: "Conflict",
      detail: "Cannot remove the last System membership during a pending move.",
      __status: 409,
    });
    const onOpenChange = vi.fn();
    renderDialog({ currentSystemName: "Payments", onOpenChange });

    fireEvent.click(screen.getByRole("button", { name: /remove from system/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Cannot remove the last System membership during a pending move."),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
