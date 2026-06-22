import { useCallback, useEffect, useMemo, useState } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

/**
 * Spec-driven filter state for list pages (ADR-0107). Composes useListUrlState:
 * text input updates a local draft; the committed value (URL + query) only
 * changes when the user explicitly calls submit() (Enter or Search button).
 * `queryFilters` is what the list query hook spreads — committed values only,
 * undefined when empty (so the unfiltered query key matches the pre-filter key).
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, never, string>, "textFilters" | "setTextFilter">,
) {
  const textSpecs = useMemo(() => specs.filter(s => s.type === "text"), [specs]);
  const committed = urlState.textFilters;

  const [draft, setDraftState] = useState<Record<string, string>>(
    () => Object.fromEntries(textSpecs.map((s) => [s.key, committed[s.key] ?? ""])) as Record<string, string>,
  );

  // Adopt the committed value when it changes from outside this hook (back/forward,
  // shared link, clearAll). After our own submit, committed === draft so this is a no-op.
  useEffect(() => {
    setDraftState(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of textSpecs) {
        const c = committed[s.key] ?? "";
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }, [committed, textSpecs]);

  const setDraft = useCallback(
    (key: string, value: string) => setDraftState(prev => ({ ...prev, [key]: value })),
    [],
  );

  const bind = useCallback(
    (key: string) => ({
      value: draft[key] ?? "",
      onChange: (v: string) => setDraft(key, v),
    }),
    [draft, setDraft],
  );

  const submit = useCallback(() => {
    for (const s of textSpecs) {
      urlState.setTextFilter(s.key, draft[s.key] ?? "");
    }
  }, [textSpecs, urlState, draft]);

  const clearAll = useCallback(() => {
    for (const s of textSpecs) {
      urlState.setTextFilter(s.key, "");
    }
    setDraftState(Object.fromEntries(textSpecs.map(s => [s.key, ""])));
  }, [textSpecs, urlState]);

  const queryFilters = useMemo(() => {
    const out: Record<string, string | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committed[s.key] ?? "") || undefined;
    return out;
  }, [textSpecs, committed]);

  const isActive = useMemo(
    () => textSpecs.some(s => (committed[s.key] ?? "") !== ""),
    [textSpecs, committed],
  );

  const activeCount = useMemo(
    () => textSpecs.filter(s => (committed[s.key] ?? "") !== "").length,
    [textSpecs, committed],
  );

  return { values: draft, bind, submit, clearAll, isActive, activeCount, queryFilters };
}
