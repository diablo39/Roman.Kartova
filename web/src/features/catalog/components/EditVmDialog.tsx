import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { InputTags } from "@/components/base/input/input-tags";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";

import {
  editVmSchema,
  POWER_STATES,
  type EditVmInput,
} from "@/features/catalog/schemas/registerVm";
import { powerStateLabel, isPowerState } from "@/features/catalog/powerState";
import { useEditVm, type VmDetailResponse } from "@/features/catalog/api/infrastructure";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  vm: VmDetailResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edit-VM modal — mirrors RegisterVmDialog's field set (minus `teamId`, which is
 * immutable here — T5 keeps team assignment out of this endpoint) and
 * EditApplicationDialog's concurrency/error-handling shape.
 *
 * Pre-fills from the supplied `vm` via RHF `values` (re-syncs on every prop change,
 * including the post-412 refetch — no explicit useEffect needed). On submit, calls
 * `useEditVm` which sends `If-Match: "<version>"`.
 *
 * Server-error UX (mirrors EditApplicationDialog):
 *  - 400 ProblemDetails with `errors` map → fields set via `applyProblemDetailsToForm`;
 *    dialog stays open.
 *  - 412 ConcurrencyConflict → toast + dialog stays open. The hook's `onError`
 *    invalidates the detail query, so the parent page refetches and RHF `values`
 *    resyncs the form to the latest server state.
 *  - Anything else → generic toast, dialog stays open for retry.
 */
export function EditVmDialog({ vm, open, onOpenChange }: Props) {
  const mutation = useEditVm(vm.id);

  const form = useForm<EditVmInput>({
    resolver: zodResolver(editVmSchema),
    values: {
      displayName: vm.displayName,
      description: vm.description,
      provider: vm.provider ?? "",
      // `VmAttributesDto.vcpu`/`memoryGb` are wire `number | string`; the form (and
      // `positiveIntStringSchema`) always work in digit strings — mirrors
      // RegisterVmDialog's DEFAULT_VALUES. `powerState` is a bare wire string
      // (see powerState.ts) narrowed via `isPowerState`, falling back to "running"
      // for an unrecognized value rather than failing the whole prefill.
      attributes: {
        powerState: isPowerState(vm.attributes.powerState) ? vm.attributes.powerState : "running",
        os: vm.attributes.os,
        hostname: vm.attributes.hostname,
        region: vm.attributes.region,
        vcpu: String(vm.attributes.vcpu),
        memoryGb: String(vm.attributes.memoryGb),
        ipAddresses: vm.attributes.ipAddresses ?? [],
      },
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({ values, expectedVersion: vm.version });
      toast.success("Virtual machine updated");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (handled) return; // 400 — field errors set, leave dialog open.

      if (status === 412) {
        toast.error("Someone else edited this. Reload to see the latest values.");
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not update virtual machine";
      toast.error(detail);
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Edit Virtual Machine" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Edit Virtual Machine</h2>
              <p className="text-sm text-tertiary">Update the virtual machine's details. Team cannot be changed here.</p>
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

              <FormField name="provider" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Provider"
                    placeholder="AWS, Azure, on-prem…"
                    hint={fieldState.error?.message ?? "Optional — the cloud or hosting provider."}
                    isInvalid={!!fieldState.error}
                    {...field}
                    value={field.value ?? ""}
                  />
                )}
              </FormField>

              <FormField name="attributes.powerState" control={form.control}>
                {({ field, fieldState }) => (
                  <div className="flex flex-col gap-1">
                    <label htmlFor="edit-vm-power-state" className="text-sm font-medium text-secondary">
                      Power State <span className="text-error-primary">*</span>
                    </label>
                    <select
                      id="edit-vm-power-state"
                      data-testid="edit-vm-power-state-select"
                      className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                      value={field.value}
                      onChange={field.onChange}
                      onBlur={field.onBlur}
                      ref={field.ref}
                      disabled={mutation.isPending}
                      aria-invalid={!!fieldState.error}
                    >
                      {POWER_STATES.map((state) => (
                        <option key={state} value={state}>{powerStateLabel(state)}</option>
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

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                  Save Changes
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
