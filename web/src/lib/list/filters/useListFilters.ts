import { useCallback, useMemo, useState } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

/**
 * Spec-driven filter state for list pages (ADR-0107). Composes useListUrlState.
 * Text and boolean inputs update local drafts; committed values (URL + query)
 * only change on submit() (Enter or Search). `queryFilters` is what the list
 * query hook spreads — text keys are committed-or-undefined (so the unfiltered
 * key matches the pre-filter key); boolean keys are always present (default
 * false), matching the always-on-the-wire includeDecommissioned dimension.
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, string, string>,
    "textFilters" | "setTextFilter" | "booleanFilters" | "setBooleanFilter">,
) {
  const textSpecs = useMemo(() => specs.filter(s => s.type === "text"), [specs]);
  const boolSpecs = useMemo(() => specs.filter(s => s.type === "boolean"), [specs]);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;

  const [draft, setDraftState] = useState<Record<string, string>>(
    () => Object.fromEntries(textSpecs.map(s => [s.key, committedText[s.key] ?? ""])),
  );
  const [boolDraft, setBoolDraftState] = useState<Record<string, boolean>>(
    () => Object.fromEntries(boolSpecs.map(s => [s.key, committedBool?.[s.key] ?? false])),
  );

  // Adopt committed values when they change from outside (back/forward, shared
  // link, clearAll). After our own submit, committed === draft so it's a no-op.
  //
  // Render-time pattern (avoids useEffect setState-in-effect lint error): track
  // the previous memoized references and reconcile during the render pass when
  // they change (committedText / committedBool are content-memoized in
  // useListUrlState so their identity only changes on real URL mutation).
  const [prevCommittedText, setPrevCommittedText] = useState(committedText);
  const [prevTextSpecs, setPrevTextSpecs] = useState(textSpecs);
  if (committedText !== prevCommittedText || textSpecs !== prevTextSpecs) {
    setPrevCommittedText(committedText);
    setPrevTextSpecs(textSpecs);
    setDraftState(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of textSpecs) {
        const c = committedText[s.key] ?? "";
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }

  const [prevCommittedBool, setPrevCommittedBool] = useState(committedBool);
  const [prevBoolSpecs, setPrevBoolSpecs] = useState(boolSpecs);
  if (committedBool !== prevCommittedBool || boolSpecs !== prevBoolSpecs) {
    setPrevCommittedBool(committedBool);
    setPrevBoolSpecs(boolSpecs);
    setBoolDraftState(prev => {
      let changed = false;
      const next = { ...prev };
      for (const s of boolSpecs) {
        const c = committedBool?.[s.key] ?? false;
        if (c !== prev[s.key]) { next[s.key] = c; changed = true; }
      }
      return changed ? next : prev;
    });
  }

  const setDraft = useCallback(
    (key: string, value: string) => setDraftState(prev => ({ ...prev, [key]: value })), []);
  const setBoolDraft = useCallback(
    (key: string, value: boolean) => setBoolDraftState(prev => ({ ...prev, [key]: value })), []);

  const bind = useCallback(
    (key: string) => ({ value: draft[key] ?? "", onChange: (v: string) => setDraft(key, v) }),
    [draft, setDraft]);
  const bindBoolean = useCallback(
    (key: string) => ({ value: boolDraft[key] ?? false, onChange: (v: boolean) => setBoolDraft(key, v) }),
    [boolDraft, setBoolDraft]);

  const submit = useCallback(() => {
    for (const s of textSpecs) urlState.setTextFilter(s.key, draft[s.key] ?? "");
    for (const s of boolSpecs) urlState.setBooleanFilter(s.key, boolDraft[s.key] ?? false);
  }, [textSpecs, boolSpecs, urlState, draft, boolDraft]);

  const clearAll = useCallback(() => {
    for (const s of textSpecs) urlState.setTextFilter(s.key, "");
    for (const s of boolSpecs) urlState.setBooleanFilter(s.key, false);
    setDraftState(Object.fromEntries(textSpecs.map(s => [s.key, ""])));
    setBoolDraftState(Object.fromEntries(boolSpecs.map(s => [s.key, false])));
  }, [textSpecs, boolSpecs, urlState]);

  const queryFilters = useMemo(() => {
    const out: Record<string, string | boolean | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committedText[s.key] ?? "") || undefined;
    for (const s of boolSpecs) out[s.key] = committedBool?.[s.key] ?? false;
    return out;
  }, [textSpecs, boolSpecs, committedText, committedBool]);

  const isActive = useMemo(
    () => textSpecs.some(s => (committedText[s.key] ?? "") !== "")
       || boolSpecs.some(s => (committedBool?.[s.key] ?? false) === true),
    [textSpecs, boolSpecs, committedText, committedBool]);

  const activeCount = useMemo(
    () => textSpecs.filter(s => (committedText[s.key] ?? "") !== "").length
        + boolSpecs.filter(s => (committedBool?.[s.key] ?? false) === true).length,
    [textSpecs, boolSpecs, committedText, committedBool]);

  return { values: draft, bind, bindBoolean, submit, clearAll, isActive, activeCount, queryFilters };
}
