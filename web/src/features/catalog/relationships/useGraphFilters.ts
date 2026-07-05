import { useCallback, useState } from "react";
import type { GraphFilters } from "@/features/catalog/relationships/graphFilter";
import { isRelationshipKind, type RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const EMPTY: GraphFilters = { kinds: [], teamIds: [] };
const storageKey = (focusKey: string) => `graph-explorer-filters:${focusKey}`;

function read(storage: Storage, focusKey: string): GraphFilters {
  try {
    const raw = storage.getItem(storageKey(focusKey));
    if (!raw) return EMPTY;
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object") return EMPTY;
    const p = parsed as Partial<GraphFilters>;
    return {
      kinds: Array.isArray(p.kinds) ? p.kinds.filter(isRelationshipKind) : [],
      teamIds: Array.isArray(p.teamIds) ? p.teamIds.filter((t): t is string => typeof t === "string") : [],
    };
  } catch {
    return EMPTY;
  }
}

function write(storage: Storage, focusKey: string, filters: GraphFilters): void {
  try {
    storage.setItem(storageKey(focusKey), JSON.stringify(filters));
  } catch {
    /* storage unavailable (private mode / quota) — degrade to in-memory only */
  }
}

export function useGraphFilters(focusKey: string, storage: Storage = window.sessionStorage) {
  const [filters, setFilters] = useState<GraphFilters>(() => read(storage, focusKey));
  // Render-time reconcile when the focus key changes (project pattern, same as useExplorerState).
  const [prevKey, setPrevKey] = useState(focusKey);
  if (prevKey !== focusKey) {
    setPrevKey(focusKey);
    setFilters(read(storage, focusKey));
  }

  const commit = useCallback(
    (next: GraphFilters) => {
      write(storage, focusKey, next);
      setFilters(next);
    },
    [storage, focusKey],
  );

  const setKinds = useCallback((kinds: RelationshipKind[]) => commit({ ...filters, kinds }), [filters, commit]);
  const setTeamIds = useCallback((teamIds: string[]) => commit({ ...filters, teamIds }), [filters, commit]);
  const clear = useCallback(() => commit(EMPTY), [commit]);

  const isActive = filters.kinds.length > 0 || filters.teamIds.length > 0;
  const activeCount = filters.kinds.length + filters.teamIds.length;

  return { filters, setKinds, setTeamIds, clear, isActive, activeCount };
}
