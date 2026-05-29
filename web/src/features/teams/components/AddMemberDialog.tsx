import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm } from "@/components/base/form/hook-form";
import { Button } from "@/components/base/buttons/button";

import {
  addTeamMemberSchema,
  type AddTeamMemberInput,
} from "@/features/teams/schemas/addTeamMember";
import { useAddTeamMember } from "@/features/teams/api/teams";
import { UserSearchCombobox } from "@/features/users/components/UserSearchCombobox";
import type { UserSummaryResponse } from "@/features/users/api/users";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

interface Props {
  teamId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Add-team-member modal. Native <select> is used for the role (Admin/Member)
 * because the base component library does not currently expose a styled
 * Select primitive (see ADR-0094 — Untitled UI free-tier set).
 */
export function AddMemberDialog({ teamId, open, onOpenChange }: Props) {
  const mutation = useAddTeamMember(teamId);
  const form = useForm<AddTeamMemberInput>({
    resolver: zodResolver(addTeamMemberSchema),
    defaultValues: { userId: "", role: "Member" },
  });
  // Visual confirmation of the picked user — purely presentational; the form's
  // `userId` (a UUID) is the source of truth for the submitted payload.
  const [selectedUser, setSelectedUser] = useState<UserSummaryResponse | null>(null);

  useEffect(() => {
    if (!open) {
      form.reset({ userId: "", role: "Member" });
      // Sync local presentational state (Selected: …) with the form reset.
      // react-hooks/set-state-in-effect would flag this, but the existing
      // form.reset call above already writes RHF state on the same close
      // transition — keeping both writes co-located keeps the open→closed
      // semantics readable. Mirrors ChangeRoleDialog's pattern for the same
      // reason.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedUser(null);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({ userId: values.userId, role: values.role });
      toast.success("Member added");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        const detail = problem.detail ?? problem.title ?? "Could not add member";
        toast.error(detail);
      }
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Add Team Member" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Add Member</h2>
            <p className="text-sm text-tertiary">
              Search for a user and choose a role. Admins can manage team members; Members have read access.
            </p>
          </div>

          <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
            <Controller
              name="userId"
              control={form.control}
              render={({ field, fieldState }) => (
                <div className="flex flex-col gap-1.5">
                  <label className="text-sm font-medium text-secondary">
                    User <span className="text-error-primary">*</span>
                  </label>
                  <UserSearchCombobox
                    onSelect={(user) => {
                      field.onChange(user.id);
                      setSelectedUser(user);
                    }}
                    placeholder="Search by name or email…"
                  />
                  {selectedUser && (
                    <p className="text-xs text-tertiary">
                      Selected: <span className="font-medium text-secondary">{selectedUser.displayName || selectedUser.email}</span>
                      {selectedUser.displayName && ` (${selectedUser.email})`}
                    </p>
                  )}
                  {fieldState.error && (
                    <p className="text-xs text-error-primary">{fieldState.error.message}</p>
                  )}
                </div>
              )}
            />

            <Controller
              name="role"
              control={form.control}
              render={({ field, fieldState }) => (
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="role-select" className="text-sm font-medium text-secondary">
                    Role
                  </label>
                  <select
                    id="role-select"
                    className="rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary shadow-xs focus:outline-none focus:ring-2 focus:ring-brand-500"
                    value={field.value}
                    onChange={(e) => field.onChange(e.target.value)}
                    onBlur={field.onBlur}
                    name={field.name}
                    ref={field.ref}
                  >
                    <option value="Member">Member</option>
                    <option value="Admin">Admin</option>
                  </select>
                  {fieldState.error && (
                    <p className="text-xs text-error-primary">{fieldState.error.message}</p>
                  )}
                </div>
              )}
            />

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                Cancel
              </Button>
              <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                Add Member
              </Button>
            </div>
          </HookForm>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
