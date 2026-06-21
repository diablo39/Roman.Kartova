import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

const DEBOUNCE_MS = 300;

/**
 * Spec-driven filter state for list pages (ADR-0107). Composes useListUrlState:
 * the controlled input echoes the immediate local value, while the committed
 * value (URL + query) is debounced so the cursor does not reset on every
 * keystroke. `queryFilters` is what the list query hook spreads — committed
 * values only, undefined when empty (so the unfiltered query key matches the
 * pre-filter key).
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, never, string>, "textFilters" | "setTextFilter">,
) {
  const textSpecs = useMemo(() => specs.filter(s => s.type === "text"), [specs]);
  const committed = urlState.textFilters;

  const seed = useCallback(
    () => Object.fromEntries(textSpecs.map(s => [s.key, committed[s.key] ?? ""])) as Record<string, string>,
    [textSpecs, committed],
  );

  const [local, setLocal] = useState<Record<string, string>>(seed);
  const timers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

  // Adopt the committed value when it changes from outside this hook (back/forward,
  // shared link, clearAll). After our own debounced commit, committed === local so
  // this is a no-op.
  useEffect(() => {
    setLocal(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of textSpecs) {
        const c = committed[s.key] ?? "";
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }, [committed, textSpecs]);

  const onChange = useCallback(
    (key: string) => (value: string) => {
      setLocal(prev => ({ ...prev, [key]: value }));
      clearTimeout(timers.current[key]);
      timers.current[key] = setTimeout(() => urlState.setTextFilter(key, value), DEBOUNCE_MS);
    },
    [urlState],
  );

  const bind = useCallback(
    (key: string) => ({ value: local[key] ?? "", onChange: onChange(key) }),
    [local, onChange],
  );

  const clearAll = useCallback(() => {
    for (const s of textSpecs) {
      clearTimeout(timers.current[s.key]);
      urlState.setTextFilter(s.key, "");
    }
    setLocal(Object.fromEntries(textSpecs.map(s => [s.key, ""])));
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

  return { values: local, bind, clearAll, isActive, queryFilters };
}
