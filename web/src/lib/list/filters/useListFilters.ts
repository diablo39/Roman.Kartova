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
 * The committed values a list query hook threads are exposed as three typed maps —
 * `textValues`, `boolValues`, `multiValues` — so call sites read them without a cast:
 * - `textValues` (text + single-select): committed string or `undefined` (so the
 *   unfiltered key matches the pre-filter key);
 * - `boolValues`: always present, default `false`;
 * - `multiValues` (multi-select): `string[]` when non-empty, `undefined` when empty —
 *   the API client serializes the array as repeated query params.
 */
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, string, string, string>, "textFilters" | "booleanFilters" | "multiFilters">,
) {
  const textSpecs = useMemo(
    () => specs.filter(s => s.type === "text" || s.type === "single-select"),
    [specs],
  );
  const boolSpecs = useMemo(() => specs.filter(s => s.type === "boolean"), [specs]);
  const multiSpecs = useMemo(() => specs.filter(s => s.type === "multi-select"), [specs]);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;
  const committedMulti = urlState.multiFilters;

  const textValues = useMemo(() => {
    const out: Record<string, string | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committedText[s.key] ?? "") || undefined;
    return out;
  }, [textSpecs, committedText]);

  const boolValues = useMemo(() => {
    const out: Record<string, boolean> = {};
    for (const s of boolSpecs) out[s.key] = committedBool?.[s.key] ?? false;
    return out;
  }, [boolSpecs, committedBool]);

  const multiValues = useMemo(() => {
    const out: Record<string, string[] | undefined> = {};
    for (const s of multiSpecs) out[s.key] = committedMulti?.[s.key]?.length ? committedMulti[s.key] : undefined;
    return out;
  }, [multiSpecs, committedMulti]);

  const isActive = useMemo(
    () => textSpecs.some(s => (committedText[s.key] ?? "") !== "")
       || boolSpecs.some(s => (committedBool?.[s.key] ?? false) === true)
       || multiSpecs.some(s => (committedMulti?.[s.key]?.length ?? 0) > 0),
    [textSpecs, boolSpecs, multiSpecs, committedText, committedBool, committedMulti]);

  const activeCount = useMemo(
    () => textSpecs.filter(s => (committedText[s.key] ?? "") !== "").length
        + boolSpecs.filter(s => (committedBool?.[s.key] ?? false) === true).length
        + multiSpecs.filter(s => (committedMulti?.[s.key]?.length ?? 0) > 0).length,
    [textSpecs, boolSpecs, multiSpecs, committedText, committedBool, committedMulti]);

  return { textValues, boolValues, multiValues, isActive, activeCount };
}
