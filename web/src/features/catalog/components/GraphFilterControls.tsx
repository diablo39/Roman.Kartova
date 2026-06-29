// web/src/features/catalog/components/GraphFilterControls.tsx
import { MultiSelect } from "@/components/base/multi-select/multi-select";
import { Button } from "@/components/base/buttons/button";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const KIND_OPTIONS = [
  { label: "Application", value: "application" },
  { label: "Service", value: "service" },
];

export interface GraphFilterControlsProps {
  kinds: RelationshipKind[];
  teamIds: string[];
  teams: { id: string; displayName: string }[];
  activeCount: number;
  onKindsChange: (kinds: RelationshipKind[]) => void;
  onTeamIdsChange: (ids: string[]) => void;
  onClear: () => void;
}

/**
 * Canvas-overlay filter controls for the graph explorer (ADR-0040). Filters
 * apply live (no submit button) — selection is client-side instant. Reuses the
 * controlled MultiSelect; not the list-page FilterBar chrome. (E-04.F-02.S-05)
 */
export function GraphFilterControls({
  kinds,
  teamIds,
  teams,
  activeCount,
  onKindsChange,
  onTeamIdsChange,
  onClear,
}: GraphFilterControlsProps) {
  const teamOptions = teams.map((t) => ({ label: t.displayName, value: t.id }));
  const isActive = activeCount > 0;

  return (
    <div className="flex w-60 flex-col gap-2 rounded-lg bg-primary p-3 shadow-lg ring-1 ring-secondary">
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold text-secondary">
          Filters{isActive ? ` (${activeCount})` : ""}
        </span>
        {isActive && (
          <Button size="sm" color="link-gray" onClick={onClear}>
            Clear
          </Button>
        )}
      </div>
      <MultiSelect
        name="graph-kind"
        aria-label="Filter by kind"
        placeholder="Any kind"
        options={KIND_OPTIONS}
        selectedKeys={kinds}
        onChange={(v) => onKindsChange(v as RelationshipKind[])}
      />
      <MultiSelect
        name="graph-team"
        aria-label="Filter by team"
        placeholder="All teams"
        options={teamOptions}
        selectedKeys={teamIds}
        onChange={onTeamIdsChange}
      />
    </div>
  );
}
