import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useDecommissionApplication,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import { isLifecycle, lifecycleLabel } from "@/features/catalog/lifecycle";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Decommission confirmation modal — terminal-state warning.
 *
 * No form, no body — the API expects an empty POST. The user only confirms
 * the destructive action.
 *
 * Server-error UX:
 *  - 409 with `reason="before-sunset-date"` (server-supplied) and a
 *    `sunsetDate` extension → toast with the date, close.
 *  - 409 generic → toast with the server-reported `currentLifecycle`, close.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function DecommissionConfirmDialog({ application, open, onOpenChange }: Props) {
  const mutation = useDecommissionApplication(application.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync();
      toast.success(`${application.displayName} decommissioned.`);
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & {
        __status?: number;
        reason?: string;
        sunsetDate?: string;
        currentLifecycle?: string;
      };
      const status = problem.__status;

      if (status === 409) {
        if (problem.reason === "before-sunset-date" && problem.sunsetDate) {
          const dateLabel = new Date(problem.sunsetDate).toLocaleDateString();
          toast.error(`Cannot decommission before sunset date ${dateLabel}.`);
          onOpenChange(false);
          return;
        }
        const current = isLifecycle(problem.currentLifecycle)
          ? lifecycleLabel(problem.currentLifecycle)
          : "an unexpected state";
        toast.error(`Cannot decommission — current state is ${current}.`);
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not decommission application";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Decommission Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Decommission {application.displayName}?</h2>
            <p className="text-sm text-tertiary">
              This is a terminal state. The application will be hidden from default views and become read-only. This cannot be undone in the current product version.
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
              Decommission
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
