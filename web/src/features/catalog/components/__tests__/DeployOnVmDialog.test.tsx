import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from "sonner";

import { DeployOnVmDialog } from "@/features/catalog/components/DeployOnVmDialog";
import * as rel from "@/features/catalog/api/relationships";

const mutateAsync = vi.fn();

function mockDeps(results: { id: string; displayName: string }[]) {
  vi.spyOn(rel, "useCreateRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: results.map((r) => ({ kind: "infrastructure", id: r.id, displayName: r.displayName })),
    isLoading: false,
  } as never);
}

function renderDialog({ onOpenChange = vi.fn() }: { onOpenChange?: (open: boolean) => void } = {}) {
  render(
    <DeployOnVmDialog
      open
      onOpenChange={onOpenChange}
      component={{ kind: "application", id: "a1", displayName: "Checkout" }}
    />,
  );
  return { onOpenChange };
}

async function selectVm() {
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "vm-" } });
  await waitFor(() => expect(screen.getByText("vm-01")).toBeInTheDocument());
  fireEvent.click(screen.getByText("vm-01"));
}

beforeEach(() => {
  vi.restoreAllMocks();
  vi.clearAllMocks();
  mutateAsync.mockReset().mockResolvedValue({ id: "rel1" });
});

describe("DeployOnVmDialog", () => {
  it("creates a deployedOn edge from the component to the selected VM", async () => {
    mockDeps([{ id: "vm1", displayName: "vm-01" }]);
    renderDialog();

    await selectVm();

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        sourceKind: "application",
        sourceId: "a1",
        type: "deployedOn",
        targetKind: "infrastructure",
        targetId: "vm1",
      }),
    );
  });

  it("toasts success and closes when the deployment is created", async () => {
    mockDeps([{ id: "vm1", displayName: "vm-01" }]);
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });

    await selectVm();

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Checkout is now deployed on vm-01."));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("toasts the ProblemDetails message and keeps the dialog open when the create fails", async () => {
    mockDeps([{ id: "vm1", displayName: "vm-01" }]);
    mutateAsync.mockRejectedValueOnce({
      title: "Bad Request",
      detail: "That VM belongs to a different tenant.",
      __status: 422,
    });
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });

    await selectVm();

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("That VM belongs to a different tenant."));
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("closes without creating anything when Close is clicked", () => {
    mockDeps([]);
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });

    fireEvent.click(screen.getByRole("button", { name: /close/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
