import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import { useCreateRelationship, type EntityOption } from "@/features/catalog/api/relationships";
import { toastProblem } from "@/shared/forms/toastProblem";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  component: { kind: "application" | "service"; id: string; displayName: string };
}

/**
 * Create a `deployedOn` edge from a component to a VM. Non-exclusive: unlike
 * AssignSystemDialog's at-most-one System membership, a component may deploy on
 * many VMs, so there is no Remove/clear action here — each pick creates one edge
 * and closes the dialog.
 */
export function DeployOnVmDialog({ open, onOpenChange, component }: Props) {
  const mutation = useCreateRelationship();

  const deploy = async (vm: EntityOption) => {
    try {
      await mutation.mutateAsync({
        sourceKind: component.kind,
        sourceId: component.id,
        type: "deployedOn",
        targetKind: "infrastructure",
        targetId: vm.id,
      });
      toast.success(`${component.displayName} is now deployed on ${vm.displayName}.`);
      onOpenChange(false);
    } catch (err) {
      toastProblem(err, { fallback: "Could not record the deployment." });
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Deploy on VM" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Deploy {component.displayName} on a VM</h2>
            <p className="text-sm text-tertiary">Pick a virtual machine this component runs on.</p>
          </div>

          <div className="space-y-5">
            <EntitySearchCombobox kind="infrastructure" onSelect={deploy} placeholder="Search VMs…" />

            <div className="flex justify-end pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                Close
              </Button>
            </div>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
