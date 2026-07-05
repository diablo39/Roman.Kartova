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

  it("offerableTypes is dependsOn for every fixed role/kind", () => {
    expect(offerableTypes("source", "application")).toEqual(["dependsOn"]);
    expect(offerableTypes("source", "service")).toEqual(["dependsOn"]);
    expect(offerableTypes("target", "application")).toEqual(["dependsOn"]);
    expect(offerableTypes("target", "service")).toEqual(["dependsOn"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("dependsOn", "source", "application")).toEqual(["application", "service"]);
  });

  it("labels the dependsOn type", () => {
    expect(relationshipTypeLabel.dependsOn).toBe("Depends on");
  });
});
