/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs them.
  | { key: string; type: "single-select" | "multi-select" | "boolean" | "date-range"; label: string };

