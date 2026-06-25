import { describe, it, expect } from "vitest";
import {
  isAllowedPair, offerableTypes, allowedOtherKinds, relationshipTypeLabel,
} from "@/features/catalog/relationships/relationshipTypeRules";

describe("relationshipTypeRules", () => {
  it("dependsOn allows every kind pair", () => {
    for (const s of ["application", "service"] as const)
      for (const t of ["application", "service"] as const)
        expect(isAllowedPair("dependsOn", s, t)).toBe(true);
  });

  it("partOf allows only service -> application", () => {
    expect(isAllowedPair("partOf", "service", "application")).toBe(true);
    expect(isAllowedPair("partOf", "service", "service")).toBe(false);
    expect(isAllowedPair("partOf", "application", "application")).toBe(false);
    expect(isAllowedPair("partOf", "application", "service")).toBe(false);
  });

  it("offerableTypes depends on the fixed role and kind", () => {
    expect(offerableTypes("source", "application")).toEqual(["dependsOn"]);
    expect(offerableTypes("source", "service")).toEqual(["dependsOn", "partOf"]);
    expect(offerableTypes("target", "application")).toEqual(["dependsOn", "partOf"]);
    expect(offerableTypes("target", "service")).toEqual(["dependsOn"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("dependsOn", "source", "application")).toEqual(["application", "service"]);
    expect(allowedOtherKinds("partOf", "source", "service")).toEqual(["application"]);
    expect(allowedOtherKinds("partOf", "target", "application")).toEqual(["service"]);
  });

  it("labels both creatable types", () => {
    expect(relationshipTypeLabel.dependsOn).toBe("Depends on");
    expect(relationshipTypeLabel.partOf).toBe("Part of");
  });
});
