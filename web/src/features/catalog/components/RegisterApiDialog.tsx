import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";
import { z } from "zod";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerApiSchema,
  API_STYLES,
  API_STYLE_LABEL,
  type RegisterApiInput,
} from "@/features/catalog/schemas/registerApi";
import { useRegisterApi } from "@/features/catalog/api/apis";
import { useTeamsList } from "@/features/teams/api/teams";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

// teamId is selected via a plain <select> tracked in local state (mirrors
// RegisterServiceDialog), not an RHF-bound field — omit it from the resolver
// schema so its "uuid" constraint doesn't block submit while it's still "".
const scalarFieldsSchema = registerApiSchema.omit({ teamId: true });
type ScalarFieldsInput = z.infer<typeof scalarFieldsSchema>;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterApiDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterApi();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");

  const form = useForm<ScalarFieldsInput>({
    resolver: zodResolver(scalarFieldsSchema),
    defaultValues: { displayName: "", description: "", style: "rest", version: "", specUrl: "" },
  });

  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "", style: "rest", version: "", specUrl: "" });
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedTeamId("");
      setTeamError("");
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");
    const payload: RegisterApiInput = { ...values, teamId: selectedTeamId, specUrl: values.specUrl || undefined };
    try {
      await mutation.mutateAsync(payload);
      toast.success("API registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) toast.error(problem.detail ?? problem.title ?? "Failed to register API");
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register API" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register API</h2>
              <p className="text-sm text-tertiary">Add a new API to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input label="Display Name" placeholder="Orders API"
                    hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
                    isInvalid={!!fieldState.error} isRequired {...field} />
                )}
              </FormField>
              <FormField name="description" control={form.control}>
                {({ field, fieldState }) => (
                  <TextArea label="Description" rows={3} placeholder="Short summary..."
                    hint={fieldState.error?.message} isInvalid={!!fieldState.error} isRequired {...field} />
                )}
              </FormField>

              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <FormField name="style" control={form.control}>
                  {({ field }) => (
                    <div className="flex flex-col gap-1">
                      <label htmlFor="register-api-style" className="text-sm font-medium text-secondary">Style</label>
                      <select id="register-api-style" data-testid="register-api-style-select"
                        className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 bg-primary text-primary"
                        value={field.value} onChange={field.onChange} disabled={mutation.isPending}>
                        {API_STYLES.map((s) => (<option key={s} value={s}>{API_STYLE_LABEL[s]}</option>))}
                      </select>
                    </div>
                  )}
                </FormField>
                <FormField name="version" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input label="Version" placeholder="v1" hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error} isRequired {...field} />
                  )}
                </FormField>
              </div>

              <FormField name="specUrl" control={form.control}>
                {({ field, fieldState }) => (
                  <Input label="Spec URL" placeholder="https://example.com/openapi.json"
                    hint={fieldState.error?.message ?? "Optional. Absolute URL to the API spec."}
                    isInvalid={!!fieldState.error} {...field} />
                )}
              </FormField>

              <div className="flex flex-col gap-1">
                <label htmlFor="register-api-team" className="text-sm font-medium text-secondary">
                  Team <span className="text-error-primary">*</span>
                </label>
                <select id="register-api-team" data-testid="register-api-team-select"
                  className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                  value={selectedTeamId}
                  onChange={(e) => { setSelectedTeamId(e.target.value); if (e.target.value) setTeamError(""); }}
                  disabled={teamsList.isLoading || mutation.isPending} aria-invalid={!!teamError}>
                  <option value="">Select a team…</option>
                  {teams.map((t) => (<option key={t.id} value={t.id}>{t.displayName}</option>))}
                </select>
                {teamError && <p className="text-xs text-error-primary">{teamError}</p>}
                {noTeams && (
                  <p className="text-xs text-tertiary">No teams available — create a team first before registering an API.</p>
                )}
              </div>

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

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>Cancel</Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending} isDisabled={noTeams}>
                  Register API
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
