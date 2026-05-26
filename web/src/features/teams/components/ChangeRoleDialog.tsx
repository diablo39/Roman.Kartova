import { useEffect, useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useChangeTeamMemberRole } from "@/features/teams/api/teams";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  teamId: string;
  userId: string;
  currentRole: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Change-role modal. Single field: role select (Admin/Member). Native
 * <select> per the rationale in AddMemberDialog (no styled Select primitive).
 */
export function ChangeRoleDialog({ teamId, userId, currentRole, open, onOpenChange }: Props) {
  const mutation = useChangeTeamMemberRole(teamId);
  const [role, setRole] = useState<string>(currentRole);

  // Sync local state when the dialog is reopened against a different member.
  useEffect(() => {
    if (open) setRole(currentRole);
  }, [open, currentRole]);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync({ userId, role });
      toast.success("Role updated");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const detail = problem.detail ?? problem.title ?? "Could not change role";
      toast.error(detail);
    }
  };

  const dirty = role !== currentRole;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Change Member Role" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Change role</h2>
            <p className="text-sm text-tertiary">Update the team-scoped role for this member.</p>
            <p className="mt-2 font-mono text-xs text-tertiary">{userId}</p>
          </div>

          <div className="flex flex-col gap-1.5 mb-5">
            <label htmlFor="change-role-select" className="text-sm font-medium text-secondary">
              Role
            </label>
            <select
              id="change-role-select"
              className="rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary shadow-xs focus:outline-none focus:ring-2 focus:ring-brand-500"
              value={role}
              onChange={(e) => setRole(e.target.value)}
            >
              <option value="Member">Member</option>
              <option value="Admin">Admin</option>
            </select>
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
              isDisabled={!dirty}
              onClick={onConfirm}
            >
              Save
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
