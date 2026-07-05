// web/src/features/catalog/components/GraphExplorerSidebar.tsx
import { Link } from "react-router-dom";
import { useApplication } from "@/features/catalog/api/applications";
import type { ApplicationResponse } from "@/features/catalog/api/applications";
import { useService } from "@/features/catalog/api/services";
import type { ServiceResponse } from "@/features/catalog/api/services";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";
import { ENTITY_KIND_LABEL, entityDetailPath } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

type Selected = { kind: RelationshipKind; id: string };

export function GraphExplorerSidebar(props: {
  selected: Selected;
  depthFromFocus: number | null;
  isExpanded: (node: string, dir: ExpandDir) => boolean;
  atCap: boolean;
  onToggleExpand: (node: string, dir: ExpandDir) => void;
  onSetFocus: () => void;
  onClose: () => void;
}) {
  const { selected, depthFromFocus, isExpanded, atCap, onToggleExpand, onSetFocus, onClose } = props;
  const nodeKey = `${selected.kind}:${selected.id}`;
  const detailHref = entityDetailPath(selected.kind, selected.id);

  // Both hooks always called (rules of hooks); the inactive one is disabled via id="".
  const appQ = useApplication(selected.kind === "application" ? selected.id : "");
  const svcQ = useService(selected.kind === "service" ? selected.id : "");
  const active = selected.kind === "application" ? appQ : svcQ;
  const entity = active.data as ApplicationResponse | ServiceResponse | undefined;
  const lifecycle = selected.kind === "application" ? (entity as ApplicationResponse | undefined)?.lifecycle : undefined;
  const health = selected.kind === "service" ? (entity as ServiceResponse | undefined)?.health : undefined;

  const dirRow = (dir: ExpandDir, label: string) => {
    const on = isExpanded(nodeKey, dir);
    return (
      <button
        type="button"
        disabled={atCap && !on}
        onClick={() => onToggleExpand(nodeKey, dir)}
        className="w-full rounded-md border border-secondary px-3 py-1.5 text-sm text-primary disabled:opacity-50"
      >
        {on ? "Collapse" : "Expand"} {label}
      </button>
    );
  };

  return (
    <aside className="flex w-72 shrink-0 flex-col gap-3 overflow-y-auto border-l border-secondary p-4" aria-label="Node details">
      <div className="flex items-start justify-between">
        <div>
          <div className="text-sm font-semibold text-primary">{entity?.displayName ?? selected.id}</div>
          <div className="text-xs text-tertiary">{ENTITY_KIND_LABEL[selected.kind]}</div>
        </div>
        <button type="button" onClick={onClose} aria-label="Close details" className="text-tertiary">✕</button>
      </div>

      {active.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load details.</p>
      ) : (
        <dl className="space-y-1 text-sm">
          {depthFromFocus != null && (
            <div className="text-tertiary">depth {depthFromFocus} from focus</div>
          )}
          {lifecycle && <div><span className="text-tertiary">Lifecycle:</span> {lifecycle}</div>}
          {health && <div><span className="text-tertiary">Health:</span> {health}</div>}
          {entity?.description && <p className="text-secondary">{entity.description}</p>}
          {entity?.teamId && (
            <Link to={`/teams/${entity.teamId}`} className="text-xs text-brand-secondary underline">Team ↗</Link>
          )}
        </dl>
      )}

      <div className="mt-auto space-y-2">
        {dirRow("out", "dependencies")}
        {dirRow("in", "dependents")}
        <button type="button" onClick={onSetFocus} className="w-full rounded-md border border-secondary px-3 py-1.5 text-sm text-primary">
          Set as focus
        </button>
        <Link to={detailHref} className="block w-full rounded-md bg-brand-solid px-3 py-1.5 text-center text-sm text-white">
          Open page ↗
        </Link>
      </div>
    </aside>
  );
}
