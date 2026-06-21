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
  registerServiceSchema,
  endpointSchema,
  type RegisterServiceInput,
  type EndpointInput,
} from "@/features/catalog/schemas/registerService";
import { useRegisterService } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { EndpointsEditor } from "./EndpointsEditor";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

const textFieldsSchema = registerServiceSchema.pick({ displayName: true, description: true });
type TextFieldsInput = z.infer<typeof textFieldsSchema>;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterServiceDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterService();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");
  const [endpoints, setEndpoints] = useState<EndpointInput[]>([]);
  const [endpointErrors, setEndpointErrors] = useState<(string | undefined)[]>([]);

  const form = useForm<TextFieldsInput>({
    resolver: zodResolver(textFieldsSchema),
    defaultValues: { displayName: "", description: "" },
  });

  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "" });
      setSelectedTeamId("");
      setTeamError("");
      setEndpoints([]);
      setEndpointErrors([]);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");

    // Validate endpoints: skip empty-URL rows (they are dropped), keep error
    // indices aligned to the displayed `endpoints` array.
    const rowErrors: (string | undefined)[] = new Array(endpoints.length).fill(undefined);
    const filled: EndpointInput[] = [];
    let endpointsValid = true;
    endpoints.forEach((e, i) => {
      if (e.url.trim() === "") return;
      const result = endpointSchema.safeParse(e);
      if (result.success) {
        filled.push(result.data);
      } else {
        endpointsValid = false;
        rowErrors[i] = result.error.issues[0]?.message ?? "Invalid endpoint";
      }
    });
    setEndpointErrors(rowErrors);
    if (!endpointsValid) return;

    const payload: RegisterServiceInput = { ...values, teamId: selectedTeamId, endpoints: filled };

    try {
      await mutation.mutateAsync(payload);
      toast.success("Service registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        toast.error(problem.detail ?? problem.title ?? "Failed to register service");
      }
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register Service" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Service</h2>
              <p className="text-sm text-tertiary">Add a new service to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Orders Service"
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
                <label htmlFor="register-service-team" className="text-sm font-medium text-secondary">
                  Team <span className="text-error-primary">*</span>
                </label>
                <select
                  id="register-service-team"
                  data-testid="register-service-team-select"
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
                {teamError && <p className="text-xs text-error-primary">{teamError}</p>}
                {noTeams && (
                  <p className="text-xs text-tertiary">
                    No teams available — create a team first before registering a service.
                  </p>
                )}
              </div>

              <EndpointsEditor
                value={endpoints}
                onChange={setEndpoints}
                errors={endpointErrors}
                disabled={mutation.isPending}
              />

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
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending} isDisabled={noTeams}>
                  Register Service
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
