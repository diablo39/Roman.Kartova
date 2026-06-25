export type RelationshipKind = "application" | "service";
export type CreatableRelationshipType = "dependsOn" | "partOf";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  dependsOn: "Depends on",
  partOf: "Part of",
};

const CREATABLE_TYPES: CreatableRelationshipType[] = ["dependsOn", "partOf"];
const KINDS: RelationshipKind[] = ["application", "service"];

// Mirror of backend RelationshipTypeRules.IsAllowedPair (ADR-0068, creatable subset).
export function isAllowedPair(
  type: CreatableRelationshipType,
  source: RelationshipKind,
  target: RelationshipKind,
): boolean {
  switch (type) {
    case "dependsOn":
      return true;
    case "partOf":
      return source === "service" && target === "application";
  }
}

// Valid kinds for the OTHER endpoint given the chosen type and which side is fixed.
export function allowedOtherKinds(
  type: CreatableRelationshipType,
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): RelationshipKind[] {
  return KINDS.filter((other) =>
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
