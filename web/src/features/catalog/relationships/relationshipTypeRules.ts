export type RelationshipKind = "application" | "service" | "api";
export type CreatableRelationshipType =
  | "dependsOn"
  | "instanceOf"
  | "providesApiFor"
  | "consumesApiFrom";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  dependsOn: "Depends on",
  instanceOf: "Instance of",
  providesApiFor: "Provides API for",
  consumesApiFrom: "Consumes API from",
};

// `dependsOn` MUST stay first — it's the Add Relationship dialog's default type;
// existing dialog tests rely on it.
const CREATABLE_TYPES: CreatableRelationshipType[] = [
  "dependsOn",
  "instanceOf",
  "providesApiFor",
  "consumesApiFrom",
];
const ALL_KINDS: RelationshipKind[] = ["application", "service", "api"];

// Shared predicate: is this kind one the app/service-only graph UI can render? (FU-A: `api`
// and any other non-app/service kind must be filtered out before reaching graph nodes/edges.)
export function isRenderableKind(kind: string): kind is RelationshipKind {
  return kind === "application" || kind === "service";
}

// Shared predicate: is this a known relationship kind at all (app/service/api)? Distinct from
// isRenderableKind — the app/service-only graph UI still excludes `api` nodes/edges from render.
export function isRelationshipKind(kind: string): kind is RelationshipKind {
  return kind === "application" || kind === "service" || kind === "api";
}

// FE creatable subset of backend RelationshipTypeRules.IsAllowedPair (ADR-0068/ADR-0111).
// Intentionally STRICTER than backend: `dependsOn` never targets `api` (backend allows any->any
// incl. api; the UI steers API links through provides/consumes and never offers `api` as a
// dependsOn target).
export function isAllowedPair(
  type: CreatableRelationshipType,
  source: RelationshipKind,
  target: RelationshipKind,
): boolean {
  switch (type) {
    case "dependsOn":
      return (
        (source === "application" || source === "service") &&
        (target === "application" || target === "service")
      );
    case "instanceOf":
      return source === "service" && target === "application";
    case "providesApiFor":
    case "consumesApiFrom":
      return (source === "application" || source === "service") && target === "api";
  }
}

// Valid kinds for the OTHER endpoint given the chosen type and which side is fixed.
export function allowedOtherKinds(
  type: CreatableRelationshipType,
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): RelationshipKind[] {
  return ALL_KINDS.filter((other) =>
    fixedRole === "source"
      ? isAllowedPair(type, fixedKind, other)
      : isAllowedPair(type, other, fixedKind),
  );
}

// Types creatable with `fixedKind` in the `fixedRole` slot (i.e. some other-kind is valid).
export function offerableTypes(
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): CreatableRelationshipType[] {
  return CREATABLE_TYPES.filter((t) => allowedOtherKinds(t, fixedRole, fixedKind).length > 0);
}
