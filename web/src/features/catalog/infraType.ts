import type { BadgeColors } from "@/components/base/badges/badge-types";

/**
 * Infrastructure resource kind (wire-shape string). Not sourced from the
 * generated OpenAPI schema — `InfrastructureListItemResponse.type` is typed
 * as a bare `string` there (no backend enum yet;
 * `Kartova.Catalog.Domain.InfrastructureType` currently has one member,
 * `VirtualMachine` → `"virtualMachine"`). Declared locally so a casing drift
 * still fails `tsc` via the total `Record` below; extend this union when the
 * backend adds a second infrastructure type.
 */
export type InfraType = "virtualMachine";

// Typed as a total Record so a missing/extra key (e.g. a casing drift) fails `tsc`.
const LABEL: Record<InfraType, string> = {
  virtualMachine: "Virtual machine",
};

// Reuses the existing `orange` BadgeColor — no new color added to the vendored
// badge primitives (`components/base/badges/badge-types.ts` is eslint-ignored).
const COLOR: Record<InfraType, BadgeColors> = {
  virtualMachine: "orange",
};

export function infraTypeLabel(type: InfraType): string {
  return LABEL[type];
}

export function infraTypeColor(type: InfraType): BadgeColors {
  return COLOR[type];
}

/** Type-guard / coercion: narrow an unknown server string to `InfraType`. */
export function isInfraType(value: unknown): value is InfraType {
  return typeof value === "string" && Object.hasOwn(LABEL, value);
}
