import { useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useSetApplicationSuccessor,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import { SuccessorPicker } from "@/features/catalog/components/SuccessorPicker";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Set/clear successor modal — usable post-hoc while the application is
 * Deprecated (ADR-0110). PUT /applications/{id}/successor is idempotent
 * replacement (ADR-0096); `null` clears the successor.
 *
 * Server-error UX:
 *  - 422 `invalid-successor` → toast, dialog stays open for retry.
 *  - 409 (source app not Deprecated) → toast, dialog stays open.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function SetSuccessorDialog({ application, open, onOpenChange }: Props) {
  const mutation = useSetApplicationSuccessor(application.id);
  const [pendingName, setPendingName] = useState<string | null>(
    application.successorDisplayName ?? null
  );

  const handleError = (err: unknown) => {
    const problem = err as ProblemDetails & { __status?: number };
    const status = problem.__status;

    if (status === 422) {
      toast.error(problem.detail ?? problem.title ?? "That application can't be set as a successor.");
      return;
    }
    if (status === 409) {
      toast.error(problem.detail ?? problem.title ?? "Application must be Deprecated to set a successor.");
      return;
    }
    toast.error(problem.detail ?? problem.title ?? "Could not update successor.");
  };

  const handleSelect = async (id: string, displayName: string) => {
    try {
      await mutation.mutateAsync(id);
      setPendingName(displayName);
      toast.success(`Successor set to ${displayName}.`);
      onOpenChange(false);
    } catch (err) {
      handleError(err);
    }
  };

  const handleClear = async () => {
    try {
      await mutation.mutateAsync(null);
      setPendingName(null);
      toast.success("Successor cleared.");
      onOpenChange(false);
    } catch (err) {
      handleError(err);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Set Successor" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Set successor for {application.displayName}</h2>
            <p className="text-sm text-tertiary">
              Point consumers to a replacement application.
            </p>
          </div>

          <div className="space-y-5">
            <div>
              <SuccessorPicker
                selectedName={pendingName}
                excludeId={application.id}
                onSelect={handleSelect}
                onClear={handleClear}
                clearDisabled={mutation.isPending}
              />
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                Close
              </Button>
            </div>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
