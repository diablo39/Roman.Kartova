import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm } from "@/components/base/form/hook-form";
import { Button } from "@/components/base/buttons/button";

import { editVmSchema, type EditVmInput } from "@/features/catalog/schemas/registerVm";
import { isPowerState } from "@/features/catalog/powerState";
import { useEditVm, type VmDetailResponse } from "@/features/catalog/api/infrastructure";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { zodFieldPaths } from "@/shared/forms/zodFieldPaths";
import { VmFormFields } from "@/features/catalog/components/VmFormFields";

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
        zodFieldPaths(editVmSchema),
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
              <VmFormFields control={form.control} idPrefix="edit-vm" disabled={mutation.isPending} />

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
