import { useMemo, useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";

import {
  useCreateRelationship,
  type CreateRelationshipPayload,
  type EntityOption,
} from "@/features/catalog/api/relationships";
import {
  offerableTypes,
  allowedOtherKinds,
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
  type FixedRole,
} from "@/features/catalog/relationships/relationshipTypeRules";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  fixedRole: FixedRole;
  fixedEntity: { kind: RelationshipKind; id: string; displayName: string };
}

export function AddRelationshipDialog({ open, onOpenChange, fixedRole, fixedEntity }: Props) {
  const mutation = useCreateRelationship();

  const types = useMemo(
    () => offerableTypes(fixedRole, fixedEntity.kind),
    [fixedRole, fixedEntity.kind],
  );

  const [type, setType] = useState<CreatableRelationshipType>(types[0]!);
  const otherKinds = useMemo(
    () => allowedOtherKinds(type, fixedRole, fixedEntity.kind),
    [type, fixedRole, fixedEntity.kind],
  );
  const [otherKind, setOtherKind] = useState<RelationshipKind>(otherKinds[0]!);
  const [other, setOther] = useState<EntityOption | null>(null);
  const [otherError, setOtherError] = useState("");

  // Render-phase reconciliation (not an effect — avoids react-hooks/set-state-in-effect):
  // reset when the dialog closes, and snap selections back when the matrix narrows past them.
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (!open) {
      setType(types[0]!);
      setOtherKind(otherKinds[0]!);
      setOther(null);
      setOtherError("");
    }
  }
  if (!types.includes(type)) {
    setType(types[0]!);
  }
  if (!otherKinds.includes(otherKind)) {
    setOtherKind(otherKinds[0]!);
    setOther(null);
  }

  const submit = async () => {
    if (!other) {
      setOtherError(`Select a ${otherKind.toLowerCase()}`);
      return;
    }
    const payload: CreateRelationshipPayload =
      fixedRole === "source"
        ? {
            sourceKind: fixedEntity.kind,
            sourceId: fixedEntity.id,
            type,
            targetKind: other.kind,
            targetId: other.id,
          }
        : {
            sourceKind: other.kind,
            sourceId: other.id,
            type,
            targetKind: fixedEntity.kind,
            targetId: fixedEntity.id,
          };
    try {
      await mutation.mutateAsync(payload);
      toast.success("Relationship added");
      onOpenChange(false);
    } catch (err) {
      const p = err as ProblemDetails & { status?: number };
      toast.error(p.detail ?? p.title ?? "Failed to add relationship");
    }
  };

  const otherLabel = fixedRole === "source" ? "Target" : "Source";

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog
          aria-label="Add relationship"
          className="bg-primary rounded-xl shadow-xl p-6 outline-none space-y-4"
        >
          <h2 className="text-lg font-semibold text-primary">
            {fixedRole === "source" ? "Add dependency" : "Add dependent"}
          </h2>
          <p className="text-sm text-tertiary">
            {fixedRole === "source"
              ? `${fixedEntity.displayName} depends on…`
              : `…depends on ${fixedEntity.displayName}`}
          </p>

          <label className="block text-sm">
            <span className="text-secondary">Type</span>
            <select
              data-testid="relationship-type-select"
              className="mt-1 w-full rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary"
              value={type}
              onChange={(e) => setType(e.target.value as CreatableRelationshipType)}
            >
              {types.map((t) => (
                <option key={t} value={t}>
                  {relationshipTypeLabel[t]}
                </option>
              ))}
            </select>
          </label>

          <label className="block text-sm">
            <span className="text-secondary">{otherLabel} kind</span>
            <select
              data-testid="relationship-otherkind-select"
              className="mt-1 w-full rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary disabled:opacity-60"
              value={otherKind}
              disabled={otherKinds.length <= 1}
              onChange={(e) => {
                setOtherKind(e.target.value as RelationshipKind);
                setOther(null);
              }}
            >
              {otherKinds.map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </select>
          </label>

          <div className="text-sm">
            <span className="text-secondary">{otherLabel}</span>
            <div className="mt-1">
              <EntitySearchCombobox
                kind={otherKind}
                excludeId={otherKind === fixedEntity.kind ? fixedEntity.id : undefined}
                onSelect={(e) => {
                  setOther(e);
                  setOtherError("");
                }}
              />
            </div>
            {other && (
              <p className="mt-1 text-xs text-tertiary">Selected: {other.displayName}</p>
            )}
            {otherError && <p className="mt-1 text-xs text-error-primary">{otherError}</p>}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              color="secondary"
              size="sm"
              onClick={() => onOpenChange(false)}
              isDisabled={mutation.isPending}
            >
              Cancel
            </Button>
            <Button
              type="button"
              color="primary"
              size="sm"
              onClick={submit}
              isDisabled={mutation.isPending}
            >
              Add relationship
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
