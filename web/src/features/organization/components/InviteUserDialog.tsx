import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { Button } from "@/components/base/buttons/button";

import {
  inviteUserSchema,
  KARTOVA_ROLES,
  type InviteUserInput,
} from "@/features/organization/schemas/inviteUser";
import {
  useCreateInvitation,
  type CreateInvitationResponse,
} from "@/features/organization/api/invitations";
import { CopyInviteLinkBox } from "@/features/organization/components/CopyInviteLinkBox";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

// Three-way 409 error mapping per slice-9 spec §6.7. Keep this in lockstep
// with `Kartova.SharedKernel.AspNetCore.ProblemTypes.cs` — the URI strings
// are the canonical wire identifiers.
const PROBLEM_TYPE_TO_MESSAGE: Record<string, string> = {
  "https://kartova.io/problems/email-already-in-tenant":
    "That email is already a member of this tenant.",
  "https://kartova.io/problems/email-already-invited":
    "A pending invitation for this email already exists. Revoke it first to send a new one.",
  "https://kartova.io/problems/email-already-on-platform":
    "An account with that email exists on the platform but is not a tenant member. Contact support to attach them.",
  "https://kartova.io/problems/service-unavailable":
    "The identity provider is temporarily unavailable. Please retry shortly.",
};

const DEFAULT_VALUES: InviteUserInput = {
  email: "",
  role: "Member",
};

/**
 * Invite-user modal. Two views inside one dialog:
 *   1. **Form state** — RHF + zodResolver(inviteUserSchema). On submit:
 *      - 400 ProblemDetails with `errors` → field errors applied (form stays open).
 *      - 409 / 422 / 502 → toast (form stays open so the user can adjust).
 *   2. **Success state** — `<CopyInviteLinkBox>` + two buttons:
 *      - **Done** → closes the dialog.
 *      - **Invite another** → resets form + local state, returns to step 1.
 *
 * Closing the dialog via Escape/backdrop/`onOpenChange(false)` resets all
 * local state so the next open is always a clean form view.
 */
export function InviteUserDialog({ open, onOpenChange }: Props) {
  const mutation = useCreateInvitation();
  const [success, setSuccess] = useState<CreateInvitationResponse | null>(null);

  const form = useForm<InviteUserInput>({
    resolver: zodResolver(inviteUserSchema),
    defaultValues: DEFAULT_VALUES,
  });

  useEffect(() => {
    if (!open) {
      form.reset(DEFAULT_VALUES);
      setSuccess(null);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const response = await mutation.mutateAsync({
        email: values.email.trim(),
        role: values.role,
      });
      setSuccess(response);
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (handled) return; // 400 — field errors applied, form stays open.

      // Three-way 409 mapping (spec §6.7) + 502 upstream.
      if (problem.type && PROBLEM_TYPE_TO_MESSAGE[problem.type]) {
        toast.error(PROBLEM_TYPE_TO_MESSAGE[problem.type]);
        return;
      }

      if (status === 409) {
        toast.error(
          problem.detail ?? problem.title ?? "Conflict creating invitation.",
        );
        return;
      }
      if (status === 422) {
        toast.error(
          problem.detail ?? "The server rejected the invitation details.",
        );
        return;
      }
      if (status === 502) {
        toast.error(
          "The identity provider is temporarily unavailable. Please retry shortly.",
        );
        return;
      }

      toast.error(problem.detail ?? problem.title ?? "Could not create invitation");
    }
  });

  const onInviteAnother = () => {
    form.reset(DEFAULT_VALUES);
    setSuccess(null);
  };

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      isDismissable={!mutation.isPending}
    >
      <Modal className="max-w-[560px]">
        <Dialog
          aria-label="Invite User"
          className="bg-primary rounded-xl shadow-xl p-6 outline-none"
        >
          {success ? (
            <div className="space-y-6">
              <CopyInviteLinkBox
                url={success.inviteUrl}
                email={success.invitation.email}
              />
              <div className="flex justify-end gap-2">
                <Button
                  type="button"
                  color="secondary"
                  size="sm"
                  onClick={onInviteAnother}
                >
                  Invite another
                </Button>
                <Button
                  type="button"
                  color="primary"
                  size="sm"
                  onClick={() => onOpenChange(false)}
                >
                  Done
                </Button>
              </div>
            </div>
          ) : (
            <>
              <div className="space-y-1 mb-4">
                <h2 className="text-lg font-semibold text-primary">
                  Invite user
                </h2>
                <p className="text-sm text-tertiary">
                  Generate an invite link. Share it manually — email delivery
                  is coming soon.
                </p>
              </div>

              <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
                <FormField name="email" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="Email"
                      type="email"
                      placeholder="user@example.com"
                      hint={
                        fieldState.error?.message ??
                        "The address the invitee should sign in with."
                      }
                      isInvalid={!!fieldState.error}
                      isRequired
                      autoComplete="off"
                      {...field}
                    />
                  )}
                </FormField>

                <FormField name="role" control={form.control}>
                  {({ field, fieldState }) => (
                    <div className="flex flex-col gap-1.5">
                      <label
                        htmlFor="invite-role"
                        className="text-sm font-medium text-secondary"
                      >
                        Role <span className="text-error-primary">*</span>
                      </label>
                      <select
                        id="invite-role"
                        className="rounded-lg border border-secondary bg-primary px-3 py-2 text-md text-primary shadow-xs ring-1 ring-primary ring-inset focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:cursor-not-allowed disabled:opacity-50"
                        value={field.value ?? ""}
                        onChange={(e) => field.onChange(e.target.value)}
                        onBlur={field.onBlur}
                        ref={field.ref}
                        name={field.name}
                        aria-invalid={!!fieldState.error}
                      >
                        {KARTOVA_ROLES.map((role) => (
                          <option key={role} value={role}>
                            {role}
                          </option>
                        ))}
                      </select>
                      <p
                        className={
                          fieldState.error
                            ? "text-sm text-error-primary"
                            : "text-sm text-tertiary"
                        }
                      >
                        {fieldState.error?.message ??
                          "Tenant-wide role assigned on first sign-in."}
                      </p>
                    </div>
                  )}
                </FormField>

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
                    type="submit"
                    color="primary"
                    size="sm"
                    isLoading={mutation.isPending}
                  >
                    Send invite
                  </Button>
                </div>
              </HookForm>
            </>
          )}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
