export type RelationshipKind = "application" | "service" | "api";
export type CreatableRelationshipType =
  | "dependsOn"
  | "instanceOf"
  | "providesApiFor"
  | "consumesApiFrom";
export type FixedRole = "source" | "target";

// `partOf` and `deployedOn` (System membership and Infrastructure membership, ADR-0111 amended)
// are real relationship types a read path can surface — e.g. a component's Dependencies tab lists
// all its edges including the membership ones — but they are NOT creatable/deletable through the
// generic Add/Delete Relationship flow (see SystemMembersSection/AssignSystemDialog and the
// dedicated VM membership UI for their own UIs). So they get labels here without joining
// CreatableRelationshipType or CREATABLE_TYPES below.
export type RelationshipTypeLabelKey = CreatableRelationshipType | "partOf" | "deployedOn";

export const relationshipTypeLabel: Record<RelationshipTypeLabelKey, string> = {
  dependsOn: "Depends on",
  instanceOf: "Instance of",
  providesApiFor: "Provides API for",
  consumesApiFrom: "Consumes API from",
  partOf: "Part of",
  deployedOn: "Deployed on",
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

// Shared predicate: is this a known relationship kind at all (application/service/api) — the
// base for the three creatable-edge kinds. `isEntityKind` below builds on this and also accepts
// `system`; untrusted tokens that may legitimately be a System (URL graph focus via
// `parseEntityRef`, and the persisted-filter reader in useGraphFilters.ts) validate through
// `isEntityKind`, not this one.
export function isRelationshipKind(kind: string): kind is RelationshipKind {
  return kind === "application" || kind === "service" || kind === "api";
}

// Rendering/search superset of RelationshipKind. `system` is a real catalog entity that can
// appear as a relationship ENDPOINT (PartOf, ADR-0111), be searched, rendered as a graph node,
// and used as a `/graph?focus=` URL token (`parseEntityRef` accepts it, FU-A) — but it is not a
// creatable-edge kind, so it stays off RelationshipKind. `infrastructure` (VM/infra resources)
// and `environment` (deployment target) are likewise render-only here — not creatable-edge kinds.
export type EntityKind = RelationshipKind | "system" | "infrastructure" | "environment";

export function isEntityKind(kind: string): kind is EntityKind {
  return isRelationshipKind(kind) || kind === "system" || kind === "infrastructure" || kind === "environment";
}

// Kinds that can be the SOURCE of a PartOf edge (a component assignable to a System).
// Mirrors backend RelationshipTypeRules.IsPartOfSourceKind (Application | Service | Infrastructure)
// — the source of truth; there is no automated C#<->TS sync gate, so keep these aligned by hand.
// `satisfies` pins PART_OF_SOURCE_KINDS ⊆ EntityKind at compile time — every source kind must have
// an ENTITY_PATH_SEGMENT/ENTITY_KIND_LABEL entry so entityDetailPath resolves; a typo/new member fails the build.
export const PART_OF_SOURCE_KINDS = ["application", "service", "infrastructure"] as const satisfies readonly EntityKind[];
export type PartOfSourceKind = (typeof PART_OF_SOURCE_KINDS)[number];

export function isPartOfSourceKind(kind: string): kind is PartOfSourceKind {
  return (PART_OF_SOURCE_KINDS as readonly string[]).includes(kind);
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
