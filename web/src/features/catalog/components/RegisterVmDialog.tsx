import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";
import { z } from "zod";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm } from "@/components/base/form/hook-form";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import { registerVmSchema } from "@/features/catalog/schemas/registerVm";
import { useRegisterVm, type RegisterVmRequest } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { zodFieldPaths } from "@/shared/forms/zodFieldPaths";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";
import { VmFormFields } from "@/features/catalog/components/VmFormFields";

// VM-attribute schema (displayName/description/provider/attributes.*) used by RHF/zod.
// teamId is managed via separate useState and validated in the submit handler to avoid
// react-aria Form + useController controlled-select interaction issues — the same fix
// applied to RegisterApplicationDialog's team select.
const vmFieldsSchema = registerVmSchema.omit({ teamId: true });
type VmFieldsInput = z.infer<typeof vmFieldsSchema>;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_VALUES: VmFieldsInput = {
  displayName: "",
  description: "",
  provider: "",
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
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");

  const form = useForm<VmFieldsInput>({
    resolver: zodResolver(vmFieldsSchema),
    defaultValues: DEFAULT_VALUES,
  });

  useEffect(() => {
    if (!open) {
      form.reset(DEFAULT_VALUES);
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedTeamId("");
      setTeamError("");
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    // Validate teamId separately since it's managed outside RHF to avoid react-aria
    // Form + controlled-select interaction issues (mirrors RegisterApplicationDialog).
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");

    const payload: RegisterVmRequest = {
      displayName: values.displayName,
      description: values.description,
      teamId: selectedTeamId,
      provider: values.provider || null,
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
        zodFieldPaths(vmFieldsSchema),
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
              <VmFormFields
                control={form.control}
                idPrefix="register-vm"
                disabled={mutation.isPending}
                afterProvider={
                  <div className="flex flex-col gap-1">
                    <label htmlFor="register-vm-team" className="text-sm font-medium text-secondary">
                      Team <span className="text-error-primary">*</span>
                    </label>
                    <select
                      id="register-vm-team"
                      data-testid="register-vm-team-select"
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
                        No teams available — create a team first before registering a virtual machine.
                      </p>
                    )}
                  </div>
                }
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
