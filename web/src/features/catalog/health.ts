import type { components } from "@/generated/openapi";

/** Wire-shape health string, sourced from the OpenAPI codegen. */
export type Health = components["schemas"]["ServiceResponse"]["health"];

type HealthBadgeColor = "gray" | "success" | "warning" | "error";

// Typed as total Records so a missing/extra key (e.g. a casing drift) fails `tsc`.
const LABEL: Record<Health, string> = {
  unknown: "Unknown",
  healthy: "Healthy",
  degraded: "Degraded",
  unhealthy: "Unhealthy",
};

const COLOR: Record<Health, HealthBadgeColor> = {
  unknown: "gray",
  healthy: "success",
  degraded: "warning",
  unhealthy: "error",
};

export function healthLabel(health: Health): string {
  return LABEL[health];
}

export function healthColor(health: Health): HealthBadgeColor {
  return COLOR[health];
}
