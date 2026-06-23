/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  | { key: string; type: "boolean"; label: string }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs them.
  | { key: string; type: "single-select" | "multi-select" | "date-range"; label: string };

