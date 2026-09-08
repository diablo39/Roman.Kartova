import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { InputTags } from "@/components/base/input/input-tags";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerVmSchema,
  POWER_STATES,
  POWER_STATE_LABEL,
  type RegisterVmInput,
} from "@/features/catalog/schemas/registerVm";
import { useRegisterVm, type RegisterVmRequest } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_VALUES: RegisterVmInput = {
  displayName: "",
  description: "",
  teamId: "",
  attributes: {
    powerState: "running",
    os: "",
    hostname: "",
    region: "",
    vcpu: "1",
    memoryGb: "1",
    ipAddresses: [],
  },
};

export function RegisterVmDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterVm();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });

  const form = useForm<RegisterVmInput>({
    resolver: zodResolver(registerVmSchema),
    defaultValues: DEFAULT_VALUES,
  });

  useEffect(() => {
    if (!open) {
      form.reset(DEFAULT_VALUES);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    const payload: RegisterVmRequest = {
      displayName: values.displayName,
      description: values.description,
      teamId: values.teamId,
      attributes: values.attributes,
    };

    try {
      await mutation.mutateAsync(payload);
      toast.success("Virtual machine registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        toast.error(problem.detail ?? problem.title ?? "Failed to register virtual machine");
      }
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register Virtual Machine" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Virtual Machine</h2>
              <p className="text-sm text-tertiary">Add a new virtual machine to your infrastructure inventory</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="web-prod-01"
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

              <FormField name="teamId" control={form.control}>
                {({ field, fieldState }) => (
                  <div className="flex flex-col gap-1">
                    <label htmlFor="register-vm-team" className="text-sm font-medium text-secondary">
                      Team <span className="text-error-primary">*</span>
                    </label>
                    <select
                      id="register-vm-team"
                      data-testid="register-vm-team-select"
                      className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                      value={field.value}
                      onChange={field.onChange}
                      onBlur={field.onBlur}
                      ref={field.ref}
                      disabled={teamsList.isLoading || mutation.isPending}
                      aria-invalid={!!fieldState.error}
                    >
                      <option value="">Select a team…</option>
                      {teams.map((t) => (
                        <option key={t.id} value={t.id}>{t.displayName}</option>
                      ))}
                    </select>
                    {fieldState.error && <p className="text-xs text-error-primary">{fieldState.error.message}</p>}
                    {noTeams && (
                      <p className="text-xs text-tertiary">
                        No teams available — create a team first before registering a virtual machine.
                      </p>
                    )}
                  </div>
                )}
              </FormField>

              <FormField name="attributes.powerState" control={form.control}>
                {({ field, fieldState }) => (
                  <div className="flex flex-col gap-1">
                    <label htmlFor="register-vm-power-state" className="text-sm font-medium text-secondary">
                      Power State <span className="text-error-primary">*</span>
                    </label>
                    <select
                      id="register-vm-power-state"
                      data-testid="register-vm-power-state-select"
                      className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                      value={field.value}
                      onChange={field.onChange}
                      onBlur={field.onBlur}
                      ref={field.ref}
                      disabled={mutation.isPending}
                      aria-invalid={!!fieldState.error}
                    >
                      {POWER_STATES.map((state) => (
                        <option key={state} value={state}>{POWER_STATE_LABEL[state]}</option>
                      ))}
                    </select>
                    {fieldState.error && <p className="text-xs text-error-primary">{fieldState.error.message}</p>}
                  </div>
                )}
              </FormField>

              <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
                <FormField name="attributes.os" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="OS"
                      placeholder="Ubuntu 24.04"
                      hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error}
                      isRequired
                      {...field}
                    />
                  )}
                </FormField>
                <FormField name="attributes.hostname" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="Hostname"
                      placeholder="web-prod-01.internal"
                      hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error}
                      isRequired
                      {...field}
                    />
                  )}
                </FormField>
                <FormField name="attributes.region" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="Region"
                      placeholder="eu-west-1"
                      hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error}
                      isRequired
                      {...field}
                    />
                  )}
                </FormField>
                <FormField name="attributes.vcpu" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="vCPU"
                      type="number"
                      placeholder="2"
                      hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error}
                      isRequired
                      {...field}
                    />
                  )}
                </FormField>
                <FormField name="attributes.memoryGb" control={form.control}>
                  {({ field, fieldState }) => (
                    <Input
                      label="Memory (GB)"
                      type="number"
                      placeholder="4"
                      hint={fieldState.error?.message}
                      isInvalid={!!fieldState.error}
                      isRequired
                      {...field}
                    />
                  )}
                </FormField>
              </div>

              <FormField name="attributes.ipAddresses" control={form.control}>
                {({ field, fieldState }) => (
                  <InputTags
                    label="IP Addresses"
                    placeholder="10.0.0.5 — press Enter to add"
                    hint={fieldState.error?.message ?? "Press Enter after each address. At least one is required."}
                    isInvalid={!!fieldState.error}
                    isRequired
                    isDisabled={mutation.isPending}
                    value={field.value}
                    onChange={field.onChange}
                  />
                )}
              </FormField>

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
                  Register Virtual Machine
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
