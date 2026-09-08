import type { BadgeColors } from "@/components/base/badges/badge-types";

/**
 * VM power-state (wire-shape string). Not sourced from the generated OpenAPI
 * schema — `VmAttributesDto.powerState` is typed as a bare `string` there.
 * Declared locally to mirror `Kartova.Catalog.Application.VmPowerState`
 * (Running/Stopped/Suspended → camelCase on the wire) so a casing drift still
 * fails `tsc` via the total `Record` below.
 */
export type PowerState = "running" | "stopped" | "suspended";

// Typed as a total Record so a missing/extra key (e.g. a casing drift) fails `tsc`.
const LABEL: Record<PowerState, string> = {
  running: "Running",
  stopped: "Stopped",
  suspended: "Suspended",
};

const COLOR: Record<PowerState, BadgeColors> = {
  running: "success",
  stopped: "gray",
  suspended: "warning",
};

export function powerStateLabel(state: PowerState): string {
  return LABEL[state];
}

export function powerStateColor(state: PowerState): BadgeColors {
  return COLOR[state];
}

/** Type-guard / coercion: narrow an unknown server string to `PowerState`. */
export function isPowerState(value: unknown): value is PowerState {
  return typeof value === "string" && Object.hasOwn(LABEL, value);
}
