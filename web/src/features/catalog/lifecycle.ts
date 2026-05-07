import type { Lifecycle } from "@/features/catalog/api/applications";

export type LifecycleBadgeColor = "success" | "warning" | "gray";

const LABEL: Record<Lifecycle, string> = {
  active: "Active",
  deprecated: "Deprecated",
  decommissioned: "Decommissioned",
};

const COLOR: Record<Lifecycle, LifecycleBadgeColor> = {
  active: "success",
  deprecated: "warning",
  decommissioned: "gray",
};

/** Human-friendly label for a wire-shape lifecycle string ("active" → "Active"). */
export function lifecycleLabel(lifecycle: Lifecycle): string {
  return LABEL[lifecycle];
}

/** Badge color for a wire-shape lifecycle string. */
export function lifecycleColor(lifecycle: Lifecycle): LifecycleBadgeColor {
  return COLOR[lifecycle];
}

/** Type-guard / coercion: narrow an unknown server string to `Lifecycle`. */
export function isLifecycle(value: unknown): value is Lifecycle {
  return typeof value === "string" && value in LABEL;
}
