import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import { z } from "zod";
import {
  registerApplicationSchema,
  type RegisterApplicationInput,
} from "@/features/catalog/schemas/registerApplication";
import { useRegisterApplication } from "@/features/catalog/api/applications";
import { useTeamsList } from "@/features/teams/api/teams";
import { LifecycleBadge } from "./LifecycleBadge";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

// Text-only schema (displayName + description) used by RHF/zod.
// teamId is managed via separate useState and validated in the submit handler
// to avoid react-aria Form + useController controlled-select interaction issues.
const textFieldsSchema = registerApplicationSchema.omit({ teamId: true });
type TextFieldsInput = z.infer<typeof textFieldsSchema>;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterApplicationDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterApplication();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");

  const form = useForm<TextFieldsInput>({
    resolver: zodResolver(textFieldsSchema),
    defaultValues: { displayName: "", description: "" },
  });

  // useForm lives above <ModalOverlay>, so the form state survives the modal
  // unmount and this reset fires reliably on close.
  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "" });
      setSelectedTeamId("");
      setTeamError("");
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    // Validate teamId separately since it's managed outside RHF to avoid
    // react-aria Form + controlled-select interaction issues in testing.
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");

    const payload: RegisterApplicationInput = { ...values, teamId: selectedTeamId };

    try {
      await mutation.mutateAsync(payload);
      toast.success("Application registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error)
      );
      if (!handled) {
        const detail =
          problem.detail ?? problem.title ?? "Failed to register application";
        toast.error(detail);
      }
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Register Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">

          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Application</h2>
              <p className="text-sm text-tertiary">Add a new application to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Payment Gateway"
                    hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>
              <FormField name="description" control={form.control}>
                {({ field, fieldState }) => (
                  <TextArea
                    label="Description"
                    rows={3}
                    placeholder="Short summary..."
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>

              <div className="flex flex-col gap-1">
                <label htmlFor="register-team" className="text-sm font-medium text-secondary">
                  Team <span className="text-error-primary">*</span>
                </label>
                <select
                  id="register-team"
                  data-testid="register-team-select"
                  className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                  value={selectedTeamId}
                  onChange={(e) => {
                    setSelectedTeamId(e.target.value);
                    if (e.target.value) setTeamError("");
                  }}
                  disabled={teamsList.isLoading || mutation.isPending}
                  aria-invalid={!!teamError}
                >
                  <option value="">Select a team…</option>
                  {teams.map((t) => (
                    <option key={t.id} value={t.id}>{t.displayName}</option>
                  ))}
                </select>
                {teamError && (
                  <p className="text-xs text-error-primary">{teamError}</p>
                )}
                {noTeams && (
                  <p className="text-xs text-tertiary">
                    No teams available — create a team first before registering an application.
                  </p>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <p className="text-xs uppercase tracking-wide text-tertiary">Created by</p>
                  <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-secondary bg-secondary/40 px-2 py-1.5">
                    <Avatar size="xs" initials={initials} />
                    <div className="min-w-0">
                      <div className="text-sm font-medium text-primary truncate">{user?.displayName ?? "—"}</div>
                      <div className="text-xs text-tertiary truncate">{user?.email ?? ""}</div>
                    </div>
                  </div>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-wide text-tertiary">Lifecycle</p>
                  <div className="mt-1">
                    <LifecycleBadge lifecycle="active" />
                  </div>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button
                  type="submit"
                  color="primary"
                  size="sm"
                  isLoading={mutation.isPending}
                  isDisabled={noTeams}
                >
                  Register Application
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
