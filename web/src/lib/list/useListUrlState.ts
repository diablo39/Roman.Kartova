import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<TField extends string> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
}

export interface ListUrlState<TField extends string> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
}

/**
 * URL-backed sort state for list pages. Falls back to defaults when URL params
 * are absent or invalid (per ADR-0095 §6.1 — no error UI for "user typed garbage
 * in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 */
export function useListUrlState<TField extends string>(
  config: Config<TField>,
): ListUrlState<TField> {
  const [params, setParams] = useSearchParams();
  const allowed = useMemo(() => new Set<string>(config.allowedSortFields), [config.allowedSortFields]);

  const rawSortBy = params.get("sortBy") ?? "";
  const sortBy = allowed.has(rawSortBy) ? (rawSortBy as TField) : config.defaultSortBy;

  const rawOrder = params.get("sortOrder") ?? "";
  const sortOrder: SortDirection =
    rawOrder === "asc" || rawOrder === "desc" ? rawOrder : config.defaultSortOrder;

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

  return { sortBy, sortOrder, setSort };
}
