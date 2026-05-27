import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useDeleteTeam,
  type TeamDetailResponse,
  type TeamResponse,
} from "@/features/teams/api/teams";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  team: TeamDetailResponse | TeamResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Delete-team confirmation modal — destructive, terminal action.
 *
 * On 409 with an `applicationCount` extension: surface a toast naming the
 * count and keep the dialog open so the user can dismiss it themselves.
 * (Server enforces "no orphans" — applications must be reassigned first.)
 */
export function DeleteTeamConfirmDialog({ team, open, onOpenChange }: Props) {
  const mutation = useDeleteTeam(team.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync();
      toast.success(`${team.displayName} deleted.`);
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & {
        __status?: number;
        applicationCount?: number;
      };
      if (problem.__status === 409) {
        toast.error(
          `Cannot delete: ${problem.applicationCount ?? "?"} application(s) still assigned. Reassign them first.`,
        );
        return; // dialog stays open
      }
      const detail = problem.detail ?? problem.title ?? "Could not delete team";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Delete Team" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">
              Delete team '{team.displayName}'?
            </h2>
            <p className="text-sm text-tertiary">
              This permanently removes the team. Applications must be reassigned first.
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
              Delete Team
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
