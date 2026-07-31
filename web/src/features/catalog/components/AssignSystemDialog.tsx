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
  component: { kind: ComponentKind; id: string; displayName: string };
  /**
   * Id of the System the component is in today, or null when unassigned. This — not
   * `currentSystemName` — decides whether the component "has a System": the server enriches a
   * dangling/unresolvable target's display name to `""` rather than null, so branching on the
   * name would show no Remove action for an assigned-but-unresolvable System.
   */
  currentSystemId: string | null;
  /** Name of the System the component is in today, or null when unassigned. */
  currentSystemName: string | null;
}

/**
 * Put a component into a System, move it, or take it out. A component belongs to at most one
 * System (ADR-0111 amended), so selecting a System REPLACES the current membership in one
 * atomic PUT — there is no separate "unassign first" step.
 *
 * Permission gating is the caller's responsibility (Task 8's row decides whether to render the
 * trigger); this dialog does not duplicate that check.
 */
export function AssignSystemDialog({ open, onOpenChange, component, currentSystemId, currentSystemName }: Props) {
  const mutation = useSetComponentSystem();
  const hasSystem = currentSystemId !== null;

  const fail = (err: unknown) =>
    toastProblem(err, {
      // 409 component-already-in-system: a clearer message than the server's generic
      // "A concurrent write already assigned this component a System membership." detail.
      byProblemType: {
        "component-already-in-system":
          "Someone else just assigned this component to a System. Refresh and try again.",
      },
      fallback: "Could not update the System.",
    });

  const assign = async (system: EntityOption) => {
    try {
      await mutation.mutateAsync({ componentKind: component.kind, componentId: component.id, systemId: system.id });
      toast.success(`${component.displayName} is now part of ${system.displayName}.`);
      onOpenChange(false);
    } catch (err) {
      fail(err);
    }
  };

  const clear = async () => {
    try {
      await mutation.mutateAsync({ componentKind: component.kind, componentId: component.id, systemId: null });
      toast.success(`${component.displayName} removed from its System.`);
      onOpenChange(false);
    } catch (err) {
      fail(err);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Assign System" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">System for {component.displayName}</h2>
            <p className="text-sm text-tertiary">
              {hasSystem
                ? `Currently part of ${currentSystemName || "an unresolvable System"}. Selecting another System moves it.`
                : "A component belongs to at most one System."}
            </p>
          </div>

          <div className="space-y-5">
            <EntitySearchCombobox kind="system" onSelect={assign} placeholder="Search systems…" />

            <div className="flex justify-between gap-2 pt-2">
              {hasSystem ? (
                <Button
                  type="button"
                  color="secondary-destructive"
                  size="sm"
                  isDisabled={mutation.isPending}
                  onClick={clear}
                >
                  Remove from System
                </Button>
              ) : (
                <span />
              )}
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
