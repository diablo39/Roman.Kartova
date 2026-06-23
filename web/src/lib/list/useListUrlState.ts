import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
  /**
   * Optional boolean URL params. Each is read as `true` only when the URL value is
   * the string `"true"` (case-insensitive); any other value or absence yields `false`.
   * Setter writes `"true"` or removes the param entirely (no `=false` clutter in the URL).
   * (Reserved ADR-0107 control: no current production consumer — Applications' former
   * `includeDecommissioned` was replaced by a lifecycle multi-select in 2026-06.)
   */
  booleanFilters?: readonly TBoolFilter[];
  /**
   * Optional free-text URL params (e.g. ["displayNameContains"]). Read as the raw
   * string ("" when absent). Setter writes the trimmed value, or removes the param
   * when blank/whitespace (no empty `=` clutter).
   */
  textFilters?: readonly TTextFilter[];
  /**
   * Optional multi-value URL params (e.g. ["lifecycle","teamId"]). Each is read as
   * an array via `getAll` ([] when absent). Setter writes one repeated param per
   * value; an empty array removes the key entirely (blank ⇒ absent, ADR-0095).
   */
  multiFilters?: readonly TMultiFilter[];
}

export interface ListUrlState<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
> {
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
  /** Map of filter name to current array value (default []). */
  multiFilters: Readonly<Record<TMultiFilter, string[]>>;
  /**
   * Commit several text + boolean + multi filters in ONE navigation. `<FilterBar>` uses
   * this on submit: calling `setTextFilter`/`setBooleanFilter` in a loop issues
   * multiple `setParams` navigations, and react-router's functional updater reads
   * the committed (stale) location each call, so the last write clobbers the
   * earlier ones (a text filter would be wiped by a following boolean write).
   * Applying all keys against a single `prev` avoids that. Text values are
   * trimmed; blank/false/empty removes the param (no `=`/`=false` clutter).
   */
  setFilters: (updates: {
    text?: Record<string, string>;
    booleans?: Record<string, boolean>;
    multi?: Record<string, string[]>;
  }) => void;
}

/**
 * URL-backed sort + filter state for list pages. Falls back to defaults when URL
 * params are absent or invalid (per ADR-0095 — no error UI for "user typed
 * garbage in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 *
 * Three optional filter axes layer on top of sort, each driven by a config list:
 * `booleanFilters` (`?flag=true`), `textFilters` (`?q=...`), and `multiFilters`
 * (repeated params `?k=a&k=b`, read via `getAll`; empty ⇒ param absent). All commit
 * in one navigation via `setFilters`. Consumers: Teams/Members/Applications lists
 * (the Applications lifecycle + team filters use `multiFilters`).
 */
export function useListUrlState<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
>(
  config: Config<TField, TBoolFilter, TTextFilter, TMultiFilter>,
): ListUrlState<TField, TBoolFilter, TTextFilter, TMultiFilter> {
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

  const multiFiltersKey = (config.multiFilters ?? []).join(",");
  const multiFilterNames = useMemo(
    () => (config.multiFilters ?? []) as readonly TMultiFilter[],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [multiFiltersKey],
  );

  const multiFilters = useMemo(() => {
    const out = {} as Record<TMultiFilter, string[]>;
    for (const name of multiFilterNames) out[name] = params.getAll(name);
    return out;
  }, [params, multiFilterNames]);

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
    (updates: { text?: Record<string, string>; booleans?: Record<string, boolean>; multi?: Record<string, string[]> }) => {
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
        for (const [name, values] of Object.entries(updates.multi ?? {})) {
          next.delete(name);
          for (const raw of values) {
            const trimmed = raw.trim();
            if (trimmed) next.append(name, trimmed);
          }
        }
        return next;
      });
    },
    [setParams],
  );

  return { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter, textFilters, setTextFilter, multiFilters, setFilters };
}
