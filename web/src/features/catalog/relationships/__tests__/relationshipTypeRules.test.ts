import { describe, it, expect } from "vitest";
import {
  isAllowedPair, offerableTypes, allowedOtherKinds, relationshipTypeLabel,
  isRelationshipKind, isEntityKind,
} from "@/features/catalog/relationships/relationshipTypeRules";

describe("relationshipTypeRules", () => {
  it("dependsOn allows app/service pairs but never targets an api", () => {
    for (const s of ["application", "service"] as const)
      for (const t of ["application", "service"] as const)
        expect(isAllowedPair("dependsOn", s, t)).toBe(true);
    expect(isAllowedPair("dependsOn", "service", "api")).toBe(false);
    expect(isAllowedPair("dependsOn", "application", "api")).toBe(false);
  });

  it("instanceOf is service -> application only", () => {
    expect(isAllowedPair("instanceOf", "service", "application")).toBe(true);
    expect(isAllowedPair("instanceOf", "application", "service")).toBe(false);
    expect(isAllowedPair("instanceOf", "service", "api")).toBe(false);
  });

  it("providesApiFor / consumesApiFrom are {app,service} -> api only", () => {
    for (const type of ["providesApiFor", "consumesApiFrom"] as const) {
      expect(isAllowedPair(type, "application", "api")).toBe(true);
      expect(isAllowedPair(type, "service", "api")).toBe(true);
      expect(isAllowedPair(type, "api", "application")).toBe(false);
      expect(isAllowedPair(type, "application", "service")).toBe(false);
    }
  });

  it("offerableTypes differs by fixed kind and role", () => {
    expect(offerableTypes("source", "application")).toEqual(["dependsOn", "providesApiFor", "consumesApiFrom"]);
    expect(offerableTypes("source", "service")).toEqual(["dependsOn", "instanceOf", "providesApiFor", "consumesApiFrom"]);
    expect(offerableTypes("target", "application")).toEqual(["dependsOn", "instanceOf"]);
    expect(offerableTypes("target", "service")).toEqual(["dependsOn"]);
    expect(offerableTypes("source", "api")).toEqual([]);
    expect(offerableTypes("target", "api")).toEqual(["providesApiFor", "consumesApiFrom"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("dependsOn", "source", "application")).toEqual(["application", "service"]);
    expect(allowedOtherKinds("providesApiFor", "source", "service")).toEqual(["api"]);
    expect(allowedOtherKinds("instanceOf", "source", "service")).toEqual(["application"]);
  });

  it("labels every creatable type", () => {
    expect(relationshipTypeLabel.dependsOn).toBe("Depends on");
    expect(relationshipTypeLabel.instanceOf).toBe("Instance of");
    expect(relationshipTypeLabel.providesApiFor).toBe("Provides API for");
    expect(relationshipTypeLabel.consumesApiFrom).toBe("Consumes API from");
  });

  it("isRelationshipKind accepts the three kinds and rejects others", () => {
    expect(isRelationshipKind("api")).toBe(true);
    expect(isRelationshipKind("application")).toBe(true);
    expect(isRelationshipKind("broker")).toBe(false);
  });

  it("treats system as an entity kind but NOT a relationship kind", () => {
    expect(isEntityKind("system")).toBe(true);
    expect(isEntityKind("application")).toBe(true);
    expect(isEntityKind("nope")).toBe(false);
    // The creatable-edge matrix stays on RelationshipKind — `system` is a real graph node and a
    // valid `/graph?focus=` token (both via EntityKind/isEntityKind, FU-A), but never a creatable
    // relationship endpoint.
    expect(isRelationshipKind("system")).toBe(false);
  });

  it("never offers partOf as a creatable type (membership is written by PUT .../system)", () => {
    expect(offerableTypes("source", "application")).not.toContain("partOf");
    expect(offerableTypes("source", "service")).not.toContain("partOf");
  });
});
