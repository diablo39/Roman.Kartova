import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useRemoveTeamMember } from "@/features/teams/api/teams";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  teamId: string;
  userId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Remove-team-member confirmation modal. Plain confirm — no form.
 */
export function RemoveMemberConfirmDialog({ teamId, userId, open, onOpenChange }: Props) {
  const mutation = useRemoveTeamMember(teamId);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync(userId);
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
        <Dialog aria-label="Remove Team Member" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Remove member?</h2>
            <p className="text-sm text-tertiary">
              The user will lose any team-scoped permissions tied to this membership.
              They can be re-added later.
            </p>
            <p className="mt-2 font-mono text-xs text-tertiary">{userId}</p>
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
