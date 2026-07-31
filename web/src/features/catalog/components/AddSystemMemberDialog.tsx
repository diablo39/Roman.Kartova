import { useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import { useSetComponentSystem, type ComponentKind } from "@/features/catalog/api/systems";
import type { EntityOption } from "@/features/catalog/api/relationships";
import { toastProblem } from "@/shared/forms/toastProblem";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  system: { id: string; displayName: string };
}

const KINDS: { value: ComponentKind; label: string }[] = [
  { value: "application", label: "Application" },
  { value: "service", label: "Service" },
];

/**
 * Add a component to this System from the System's Members tab. Writes through the same
 * at-most-one setter as the component-side dialog (`AssignSystemDialog`), so assigning a
 * component that already belongs elsewhere MOVES it (no 409) — the toast says so.
 */
export function AddSystemMemberDialog({ open, onOpenChange, system }: Props) {
  const [kind, setKind] = useState<ComponentKind>("application");
  const mutation = useSetComponentSystem();

  const assign = async (component: EntityOption) => {
    try {
      await mutation.mutateAsync({ componentKind: kind, componentId: component.id, systemId: system.id });
      toast.success(`${component.displayName} is now part of ${system.displayName}.`);
      onOpenChange(false);
    } catch (err) {
      // 409 component-already-in-system: a clearer message than the server's generic
      // "A concurrent write already assigned this component a System membership." detail.
      toastProblem(err, {
        byProblemType: {
          "component-already-in-system":
            "Someone else just assigned this component to a System. Refresh and try again.",
        },
        fallback: "Could not add the component.",
      });
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Assign component" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Assign a component to {system.displayName}</h2>
            <p className="text-sm text-tertiary">
              A component belongs to at most one System — assigning one that is already in another System moves it.
            </p>
          </div>

          <div className="space-y-5">
            <div role="radiogroup" aria-label="Component kind" className="flex gap-2">
              {KINDS.map((k) => (
                <label key={k.value} className="flex items-center gap-1 text-sm text-secondary">
                  <input
                    type="radio"
                    name="component-kind"
                    value={k.value}
                    checked={kind === k.value}
                    onChange={() => setKind(k.value)}
                  />
                  {k.label}
                </label>
              ))}
            </div>

            <EntitySearchCombobox kind={kind} onSelect={assign} placeholder={`Search ${kind}s…`} />

            <div className="flex justify-end gap-2 pt-2">
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
