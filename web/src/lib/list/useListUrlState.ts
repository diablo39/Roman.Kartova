import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<TField extends string, TBoolFilter extends string = never> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
  /**
   * Optional boolean URL params (e.g. `["includeDecommissioned"]`). Each is
   * read as `true` only when the URL value is the string `"true"` (case-insensitive);
   * any other value or absence yields `false`. Setter writes `"true"` or removes
   * the param entirely (no `=false` clutter in the URL).
   */
  booleanFilters?: readonly TBoolFilter[];
}

export interface ListUrlState<TField extends string, TBoolFilter extends string = never> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
  /** Map of filter name to current boolean value (default false). */
  booleanFilters: Record<TBoolFilter, boolean>;
  setBooleanFilter: (name: TBoolFilter, value: boolean) => void;
}

/**
 * URL-backed sort + filter state for list pages. Falls back to defaults when URL
 * params are absent or invalid (per ADR-0095 §6.1 — no error UI for "user typed
 * garbage in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 *
 * Slice 6: optional boolean filters supported via the `booleanFilters` config —
 * used by the Catalog list page for `?includeDecommissioned=true`.
 */
export function useListUrlState<TField extends string, TBoolFilter extends string = never>(
  config: Config<TField, TBoolFilter>,
): ListUrlState<TField, TBoolFilter> {
  const [params, setParams] = useSearchParams();
  const allowed = useMemo(() => new Set<string>(config.allowedSortFields), [config.allowedSortFields]);
  const boolFilterNames = useMemo(
    () => (config.booleanFilters ?? []) as readonly TBoolFilter[],
    [config.booleanFilters],
  );

  const rawSortBy = params.get("sortBy") ?? "";
  const sortBy = allowed.has(rawSortBy) ? (rawSortBy as TField) : config.defaultSortBy;

  const rawOrder = params.get("sortOrder") ?? "";
  const sortOrder: SortDirection =
    rawOrder === "asc" || rawOrder === "desc" ? rawOrder : config.defaultSortOrder;

  const booleanFilters = useMemo(() => {
    const out = {} as Record<TBoolFilter, boolean>;
    for (const name of boolFilterNames) {
      out[name] = params.get(name)?.toLowerCase() === "true";
    }
    return out;
  }, [params, boolFilterNames]);

  const setSort = useCallback(
    (field: TField, order: SortDirection) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        next.set("sortBy", field);
        next.set("sortOrder", order);
        return next;
      });
    },
    [setParams],
  );

  const setBooleanFilter = useCallback(
    (name: TBoolFilter, value: boolean) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        if (value) {
          next.set(name, "true");
        } else {
          next.delete(name);
        }
        return next;
      });
    },
    [setParams],
  );

  return { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter };
}
