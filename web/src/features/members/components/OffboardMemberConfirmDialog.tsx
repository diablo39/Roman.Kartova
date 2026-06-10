import { useEffect, useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import { useOffboardMember } from "@/features/members/api/members";
import { UserSearchCombobox } from "@/features/users/components/UserSearchCombobox";
import type { UserSummaryResponse } from "@/features/users/api/users";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  userId: string;
  displayName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Offboard-member confirmation modal. Requires selecting a successor user to
 * reassign the departing member's applications to before removing their account.
 * Surfaces 409 last-orgadmin / cannot-offboard-self / 422 invalid-successor
 * messages via toast.
 */
export function OffboardMemberConfirmDialog({ userId, displayName, open, onOpenChange }: Props) {
  const mutation = useOffboardMember();
  const [successor, setSuccessor] = useState<UserSummaryResponse | null>(null);

  // Reset successor selection whenever the dialog closes — mirrors AddMemberDialog's
  // reset-on-close pattern so stale picks don't leak to subsequent opens.
  useEffect(() => {
    if (!open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSuccessor(null);
    }
  }, [open]);

  const onConfirm = async () => {
    if (!successor) return;
    try {
      await mutation.mutateAsync({ userId, successorUserId: successor.id });
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
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Remove member?</h2>
            <p className="text-sm text-tertiary">
              Removing <span className="font-medium text-secondary">{displayName}</span> reassigns
              all their applications to the chosen successor and permanently deletes their account.
            </p>
          </div>

          <div className="flex flex-col gap-1.5 mb-5">
            <label className="text-sm font-medium text-secondary">
              Successor <span className="text-error-primary">*</span>
            </label>
            <UserSearchCombobox
              onSelect={(user) => setSuccessor(user)}
              placeholder="Search for a successor…"
              excludeUserId={userId}
            />
            {successor && (
              <p className="text-xs text-tertiary">
                Selected:{" "}
                <span className="font-medium text-secondary">
                  {successor.displayName ? `${successor.displayName} (${successor.email})` : successor.email}
                </span>
              </p>
            )}
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
              isDisabled={!successor}
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
