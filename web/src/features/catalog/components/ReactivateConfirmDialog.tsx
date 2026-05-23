import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useReactivateApplication,
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
 * Reactivate confirmation modal — reverse-lifecycle action (OrgAdmin only).
 *
 * No form, no body — the API expects an empty POST. Transitions:
 *   Deprecated → Active
 *   Decommissioned → Active
 *
 * Server-error UX:
 *  - 409 LifecycleConflict → toast with the server-reported `currentLifecycle`, close.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function ReactivateConfirmDialog({ application, open, onOpenChange }: Props) {
  const mutation = useReactivateApplication(application.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync();
      toast.success(`${application.displayName} reactivated. Application returned to Active.`);
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & {
        __status?: number;
        currentLifecycle?: string;
      };
      const status = problem.__status;

      if (status === 409) {
        const current = isLifecycle(problem.currentLifecycle)
          ? lifecycleLabel(problem.currentLifecycle)
          : "an unexpected state";
        toast.error(`Cannot reactivate — current state is ${current}.`);
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not reactivate application";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Reactivate Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Reactivate {application.displayName}?</h2>
            <p className="text-sm text-tertiary">
              Reactivate <strong>{application.displayName}</strong>? The application returns to{" "}
              <strong>Active</strong> and its sunset date is cleared.
            </p>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              color="primary"
              size="sm"
              isLoading={mutation.isPending}
              onClick={onConfirm}
            >
              Reactivate
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
