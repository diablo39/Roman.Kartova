import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";

interface Props {
  /** Display name of the currently-picked successor, or null when none is set. */
  selectedName: string | null;
  /** Application id to exclude from search results (self cannot be a successor). */
  excludeId: string;
  onSelect: (id: string, displayName: string) => void;
  onClear: () => void;
  /** Disables the Clear affordance while a mutation is in flight. */
  clearDisabled?: boolean;
}

/**
 * Successor picker: shows the picked application as a chip with a Clear
 * affordance, or an {@link EntitySearchCombobox} to pick one. Shared by
 * DeprecateConfirmDialog (successor at deprecate time) and SetSuccessorDialog
 * (post-hoc set/clear) — ADR-0110. The two callers differ only in how
 * they commit the selection (react-hook-form value vs. a mutation).
 */
export function SuccessorPicker({
  selectedName,
  excludeId,
  onSelect,
  onClear,
  clearDisabled,
}: Props) {
  if (selectedName) {
    return (
      <div className="flex items-center justify-between rounded-lg border border-secondary px-3 py-2 text-sm">
        <span className="text-primary">{selectedName}</span>
        <button
          type="button"
          className="text-tertiary hover:text-primary"
          onClick={onClear}
          disabled={clearDisabled}
        >
          Clear
        </button>
      </div>
    );
  }

  return (
    <EntitySearchCombobox
      kind="application"
      excludeId={excludeId}
      onSelect={(entity) => onSelect(entity.id, entity.displayName)}
    />
  );
}
