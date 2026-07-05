export type RelationshipKind = "application" | "service";
export type CreatableRelationshipType = "dependsOn";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  dependsOn: "Depends on",
};

const CREATABLE_TYPES: CreatableRelationshipType[] = ["dependsOn"];
const KINDS: RelationshipKind[] = ["application", "service"];

// Mirror of backend RelationshipTypeRules.IsAllowedPair (ADR-0068, creatable UI subset).
// Only `dependsOn` is creatable from the UI this slice; API edge types (providesApiFor,
// consumesApiFrom, instanceOf) and the `api` kind land with the API graph UI (FU-A).
export function isAllowedPair(
  _type: CreatableRelationshipType,
  _source: RelationshipKind,
  _target: RelationshipKind,
): boolean {
  return true; // dependsOn: any → any
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
