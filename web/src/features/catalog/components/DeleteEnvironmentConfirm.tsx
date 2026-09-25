import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useDeleteEnvironment, type EnvironmentDetailResponse } from "@/features/catalog/api/environments";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  environment: EnvironmentDetailResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Called after a successful delete — the caller navigates away (the resource is gone). */
  onDeleted: () => void;
}

/**
 * Delete-Environment confirmation modal (A2) — destructive, terminal action, mirrors
 * `DeleteVmConfirm` exactly (hard delete, no lifecycle/soft-delete for Environment).
 *
 * Sends `If-Match: "<version>"`. On 412 (stale version — someone else edited/deleted
 * first) the dialog stays open with a "reload" message; the caller is responsible for
 * gating this dialog behind the `catalog.environments.delete` permission.
 */
export function DeleteEnvironmentConfirm({ environment, open, onOpenChange, onDeleted }: Props) {
  const mutation = useDeleteEnvironment(environment.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync(environment.version);
      toast.success(`${environment.displayName} deleted.`);
      onOpenChange(false);
      onDeleted();
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      if (problem.__status === 412) {
        toast.error("Someone else changed this environment. Reload to see the latest state.");
        return; // dialog stays open
      }
      const detail = problem.detail ?? problem.title ?? "Could not delete environment";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Delete Environment" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">
              Delete environment '{environment.displayName}'?
            </h2>
            <p className="text-sm text-tertiary">
              This permanently removes the environment from the catalog. This action cannot be undone.
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
              Delete Environment
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
