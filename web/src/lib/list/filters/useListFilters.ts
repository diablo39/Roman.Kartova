import { useMemo } from "react";
import type { ListUrlState } from "@/lib/list/useListUrlState";
import type { FilterSpec } from "./types";

/**
 * Committed (URL-derived) filter state for list pages (ADR-0107). This hook holds
 * NO draft/input state — it is a pure derivation of `urlState` — so typing in
 * `<FilterBar>` never re-renders the page or its table. The transient draft lives
 * in `<FilterBar>` as uncontrolled inputs that commit to the URL only on submit
 * (Enter / Search button); see ADR-0107 clause 3.
 *
 * `queryFilters` is what the list query hook spreads: text keys are
 * committed-or-`undefined` (so the unfiltered key matches the pre-filter key);
 * boolean keys are always present (default `false`), matching the always-on-the-
 * wire `includeDecommissioned` dimension.
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, string, string>, "textFilters" | "booleanFilters">,
) {
  const textSpecs = useMemo(
    () => specs.filter(s => s.type === "text" || s.type === "single-select"),
    [specs],
  );
  const boolSpecs = useMemo(() => specs.filter(s => s.type === "boolean"), [specs]);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;

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

  return { queryFilters, isActive, activeCount };
}
