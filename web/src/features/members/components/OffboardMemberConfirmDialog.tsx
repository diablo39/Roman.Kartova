import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useOffboardMember } from "@/features/members/api/members";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  userId: string;
  displayName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Offboard-member confirmation modal. Plain confirm — no successor required
 * because applications keep their "created by" attribution as history after
 * the creator is removed (ADR-0102). Surfaces 409 last-orgadmin /
 * cannot-offboard-self messages via toast.
 */
export function OffboardMemberConfirmDialog({ userId, displayName, open, onOpenChange }: Props) {
  const mutation = useOffboardMember();

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync({ userId });
      toast.success("Member removed");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const detail = problem.detail ?? problem.title ?? "Could not remove member";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Remove Member" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-6">
            <h2 className="text-lg font-semibold text-primary">Remove member?</h2>
            <p className="text-sm text-tertiary">
              Remove <span className="font-medium text-secondary">{displayName}</span>? This
              permanently deletes their account. Apps they created keep their &quot;created by&quot;
              attribution as history.
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
              Remove
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
