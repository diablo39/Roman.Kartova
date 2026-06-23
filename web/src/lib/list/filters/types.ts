/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  | { key: string; type: "boolean"; label: string }
  | { key: string; type: "single-select"; label: string; options: { label: string; value: string }[] }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs them.
  | { key: string; type: "multi-select" | "date-range"; label: string };

