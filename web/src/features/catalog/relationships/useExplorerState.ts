import { useCallback, useState } from "react";

export type ExpandDir = "out" | "in";
export type ExpandEntry = { node: string; dir: ExpandDir };
export type ExplorerState = { expand: ExpandEntry[]; selected: string | null };

const EMPTY: ExplorerState = { expand: [], selected: null };
const storageKey = (focusKey: string) => `graph-explorer:${focusKey}`;

function read(storage: Storage, focusKey: string): ExplorerState {
  try {
    const raw = storage.getItem(storageKey(focusKey));
    if (!raw) return EMPTY;
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object" || !Array.isArray((parsed as ExplorerState).expand)) return EMPTY;
    const p = parsed as ExplorerState;
    return { expand: p.expand, selected: p.selected ?? null };
  } catch {
    return EMPTY;
  }
}

function write(storage: Storage, focusKey: string, state: ExplorerState): void {
  try {
    storage.setItem(storageKey(focusKey), JSON.stringify(state));
  } catch {
    /* storage unavailable (private mode / quota) — degrade to in-memory only */
  }
}

export function useExplorerState(
  focusKey: string,
  storage: Storage = window.sessionStorage,
) {
  const [state, setState] = useState<ExplorerState>(() => read(storage, focusKey));
  // Render-time reconcile when the focus key changes (project pattern: derive
  // state from props in render with a prev-value guard, not in an effect).
  const [prevKey, setPrevKey] = useState(focusKey);
  if (prevKey !== focusKey) {
    setPrevKey(focusKey);
    setState(read(storage, focusKey));
  }

  const commit = useCallback(
    (next: ExplorerState) => {
      write(storage, focusKey, next);
      setState(next);
    },
    [storage, focusKey],
  );

  const isExpanded = useCallback(
    (node: string, dir: ExpandDir) => state.expand.some((e) => e.node === node && e.dir === dir),
    [state.expand],
  );
  const toggleExpand = useCallback(
    (node: string, dir: ExpandDir) => {
      const exists = state.expand.some((e) => e.node === node && e.dir === dir);
      const expand = exists
        ? state.expand.filter((e) => !(e.node === node && e.dir === dir))
        : [...state.expand, { node, dir }];
      commit({ ...state, expand });
    },
    [state, commit],
  );
  const select = useCallback((node: string | null) => commit({ ...state, selected: node }), [state, commit]);
  const reset = useCallback(() => commit({ expand: [], selected: null }), [commit]);

  return { expand: state.expand, selected: state.selected, isExpanded, toggleExpand, select, reset };
}
