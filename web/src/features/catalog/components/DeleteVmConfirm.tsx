import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useDeleteVm, type VmDetailResponse } from "@/features/catalog/api/infrastructure";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  vm: VmDetailResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called after a successful delete — the caller navigates away (the resource is gone). */
  onDeleted: () => void;
}

/**
 * Delete-VM confirmation modal — destructive, terminal action (T6, ADR-0111
 * amendment: hard delete, no lifecycle/soft-delete for infrastructure).
 *
 * A react-aria overlay, never a native `confirm()` — see CLAUDE.md's Table/overlay
 * gotcha: any overlay opened while the underlying `<Table>` has lost its single
 * `isRowHeader` column blank-pages the screen, so this must go through the same
 * `Dialog` machinery as every other confirm in this codebase.
 *
 * Sends `If-Match: "<version>"` (mirrors `useEditVm`/`useEditApplication`). On 412
 * (stale version — someone else edited/deleted first) the dialog stays open with a
 * "reload" message; the caller is responsible for gating this dialog behind the
 * `catalog.infrastructure.delete` permission (mirrors how `VmDetailPage`/
 * `ApplicationDetailPage` gate their Edit actions, rather than each dialog
 * re-checking permissions itself).
 */
export function DeleteVmConfirm({ vm, open, onOpenChange, onDeleted }: Props) {
  const mutation = useDeleteVm(vm.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync(vm.version);
      toast.success(`${vm.displayName} deleted.`);
      onOpenChange(false);
      onDeleted();
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      if (problem.__status === 412) {
        toast.error("Someone else changed this virtual machine. Reload to see the latest state.");
        return; // dialog stays open
      }
      const detail = problem.detail ?? problem.title ?? "Could not delete virtual machine";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Delete Virtual Machine" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">
              Delete virtual machine '{vm.displayName}'?
            </h2>
            <p className="text-sm text-tertiary">
              This permanently removes the virtual machine from the infrastructure inventory.
              This action cannot be undone.
            </p>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              color="primary-destructive"
              size="sm"
              isLoading={mutation.isPending}
              onClick={onConfirm}
            >
              Delete Virtual Machine
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
