import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<TField extends string, TBoolFilter extends string = never, TTextFilter extends string = never> {
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
  /**
   * Optional free-text URL params (e.g. ["displayNameContains"]). Read as the raw
   * string ("" when absent). Setter writes the trimmed value, or removes the param
   * when blank/whitespace (no empty `=` clutter).
   */
  textFilters?: readonly TTextFilter[];
}

export interface ListUrlState<TField extends string, TBoolFilter extends string = never, TTextFilter extends string = never> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
  /** Map of filter name to current boolean value (default false). */
  booleanFilters: Readonly<Record<TBoolFilter, boolean>>;
  /**
   * Accepts any string key so generic consumers (e.g. useListFilters, which is
   * string-keyed via FilterSpec) can drive it without a cast. Read-side literal
   * keys (booleanFilters map) retain their narrowed type.
   */
  setBooleanFilter: (name: string, value: boolean) => void;
  /** Map of filter name to current raw string value (default ""). */
  textFilters: Readonly<Record<TTextFilter, string>>;
  /**
   * Accepts any string key so generic consumers (e.g. useListFilters, which is
   * string-keyed via FilterSpec) can drive it without a cast. Read-side literal
   * keys (textFilters map) retain their narrowed type.
   */
  setTextFilter: (name: string, value: string) => void;
  /**
   * Commit several text + boolean filters in ONE navigation. `<FilterBar>` uses
   * this on submit: calling `setTextFilter`/`setBooleanFilter` in a loop issues
   * multiple `setParams` navigations, and react-router's functional updater reads
   * the committed (stale) location each call, so the last write clobbers the
   * earlier ones (a text filter would be wiped by a following boolean write).
   * Applying all keys against a single `prev` avoids that. Text values are
   * trimmed; blank/false removes the param (no `=`/`=false` clutter).
   */
  setFilters: (updates: { text?: Record<string, string>; booleans?: Record<string, boolean> }) => void;
}

/**
 * URL-backed sort + filter state for list pages. Falls back to defaults when URL
 * params are absent or invalid (per ADR-0095 — no error UI for "user typed
 * garbage in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 *
 * Slice 6: optional boolean filters supported via the `booleanFilters` config —
 * used by the Catalog list page for `?includeDecommissioned=true`.
 * Slice 7: optional text filters supported via the `textFilters` config —
 * used by the Teams list page for `?displayNameContains=...`.
 */
export function useListUrlState<TField extends string, TBoolFilter extends string = never, TTextFilter extends string = never>(
  config: Config<TField, TBoolFilter, TTextFilter>,
): ListUrlState<TField, TBoolFilter, TTextFilter> {
  const [params, setParams] = useSearchParams();
  const allowed = useMemo(() => new Set<string>(config.allowedSortFields), [config.allowedSortFields]);
  const boolFiltersKey = (config.booleanFilters ?? []).join(",");
  const boolFilterNames = useMemo(
    () => (config.booleanFilters ?? []) as readonly TBoolFilter[],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [boolFiltersKey],
  );
  const textFiltersKey = (config.textFilters ?? []).join(",");
  const textFilterNames = useMemo(
    () => (config.textFilters ?? []) as readonly TTextFilter[],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [textFiltersKey],
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

  const textFilters = useMemo(() => {
    const out = {} as Record<TTextFilter, string>;
    for (const name of textFilterNames) {
      out[name] = params.get(name) ?? "";
    }
    return out;
  }, [params, textFilterNames]);

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
    (name: string, value: boolean) => {
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

  const setTextFilter = useCallback(
    (name: string, value: string) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        const trimmed = value.trim();
        if (trimmed) {
          next.set(name, trimmed);
        } else {
          next.delete(name);
        }
        return next;
      });
    },
    [setParams],
  );

  const setFilters = useCallback(
    (updates: { text?: Record<string, string>; booleans?: Record<string, boolean> }) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        for (const [name, value] of Object.entries(updates.text ?? {})) {
          const trimmed = value.trim();
          if (trimmed) next.set(name, trimmed);
          else next.delete(name);
        }
        for (const [name, value] of Object.entries(updates.booleans ?? {})) {
          if (value) next.set(name, "true");
          else next.delete(name);
        }
        return next;
      });
    },
    [setParams],
  );

  return { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter, textFilters, setTextFilter, setFilters };
}
