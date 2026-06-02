import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useRevokeInvitation,
  type InvitationResponse,
} from "@/features/organization/api/invitations";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  /**
   * The invitation to revoke. `null` when the dialog is closed; rendered as an
   * empty overlay so the parent doesn't have to conditionally mount.
   */
  invitation: InvitationResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Confirmation modal for revoking a pending invitation (slice-9 spec §6.7).
 *
 * Failure surface — every branch logs a user-facing toast:
 *   - 404 → "Invitation was already gone" + close. The user's intent ("make
 *           this go away") is already satisfied; the underlying revoke hook's
 *           `onSuccess` does not run on error, so we cannot rely on it to
 *           invalidate the list — that's fine because the parent page only
 *           refetches on next mount or explicit reset. The 404 branch matches
 *           the team-member precedent (RemoveMemberConfirmDialog).
 *   - 409 → "Invitation is no longer pending" + close. End state for the user.
 *   - 502 → "Identity provider unavailable" + dialog stays open so they can
 *           retry shortly without re-finding the row.
 *   - default → toast `problem.detail` + dialog stays open.
 *
 * The hook itself attaches `__status` to thrown errors and invalidates
 * `invitationKeys.all` only on success — that's why the page picks up the
 * change after a successful revoke. After a 404/409 close we accept a
 * transiently-stale list until the next focus/refetch; the alternative
 * (manual invalidate here) would race with mid-flight pagination.
 */
export function RevokeInvitationConfirm({ invitation, open, onOpenChange }: Props) {
  // Always create the mutation — keyed on the invitation id when present, or
  // a sentinel when not, so the hook order stays stable across renders. The
  // empty-id branch never fires because the button is hidden when there's no
  // invitation to act on.
  const mutation = useRevokeInvitation(invitation?.id ?? "");

  const onConfirm = async () => {
    if (!invitation) return;
    try {
      await mutation.mutateAsync();
      toast.success("Invitation revoked");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      if (status === 404) {
        // The invitation no longer exists from the server's point of view —
        // the user's intent is already satisfied. Treat as success.
        toast.success("Invitation was already gone");
        onOpenChange(false);
        return;
      }
      if (status === 409) {
        // Pending → some terminal state happened between page load and click.
        toast.error("Invitation is no longer pending");
        onOpenChange(false);
        return;
      }
      if (status === 502) {
        // Upstream KeyCloak failed — keep the dialog open so the user can retry.
        toast.error("Identity provider unavailable — try again shortly");
        return;
      }

      toast.error(problem.detail ?? problem.title ?? "Could not revoke invitation");
    }
  };

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      isDismissable={!mutation.isPending}
    >
      <Modal className="max-w-[480px]">
        <Dialog
          aria-label="Revoke Invitation"
          className="bg-primary rounded-xl shadow-xl p-6 outline-none"
        >
          {invitation && (
            <>
              <div className="space-y-1 mb-4">
                <h2 className="text-lg font-semibold text-primary">Revoke invitation?</h2>
                <p className="text-sm text-tertiary">
                  Revoke invitation for{" "}
                  <span className="font-medium text-primary">{invitation.email}</span>?
                  This action cannot be undone.
                </p>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button
                  type="button"
                  color="secondary"
                  size="sm"
                  onClick={() => onOpenChange(false)}
                  isDisabled={mutation.isPending}
                >
                  Cancel
                </Button>
                <Button
                  type="button"
                  color="primary-destructive"
                  size="sm"
                  isLoading={mutation.isPending}
                  onClick={onConfirm}
                >
                  Revoke
                </Button>
              </div>
            </>
          )}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
