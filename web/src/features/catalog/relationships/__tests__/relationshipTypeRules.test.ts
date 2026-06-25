import { describe, it, expect } from "vitest";
import {
  isAllowedPair, offerableTypes, allowedOtherKinds, relationshipTypeLabel,
} from "@/features/catalog/relationships/relationshipTypeRules";

describe("relationshipTypeRules", () => {
  it("DependsOn allows every kind pair", () => {
    for (const s of ["Application", "Service"] as const)
      for (const t of ["Application", "Service"] as const)
        expect(isAllowedPair("DependsOn", s, t)).toBe(true);
  });

  it("PartOf allows only Service -> Application", () => {
    expect(isAllowedPair("PartOf", "Service", "Application")).toBe(true);
    expect(isAllowedPair("PartOf", "Service", "Service")).toBe(false);
    expect(isAllowedPair("PartOf", "Application", "Application")).toBe(false);
    expect(isAllowedPair("PartOf", "Application", "Service")).toBe(false);
  });

  it("offerableTypes depends on the fixed role and kind", () => {
    expect(offerableTypes("source", "Application")).toEqual(["DependsOn"]);
    expect(offerableTypes("source", "Service")).toEqual(["DependsOn", "PartOf"]);
    expect(offerableTypes("target", "Application")).toEqual(["DependsOn", "PartOf"]);
    expect(offerableTypes("target", "Service")).toEqual(["DependsOn"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("DependsOn", "source", "Application")).toEqual(["Application", "Service"]);
    expect(allowedOtherKinds("PartOf", "source", "Service")).toEqual(["Application"]);
    expect(allowedOtherKinds("PartOf", "target", "Application")).toEqual(["Service"]);
  });

  it("labels both creatable types", () => {
    expect(relationshipTypeLabel.DependsOn).toBe("Depends on");
    expect(relationshipTypeLabel.PartOf).toBe("Part of");
  });
});
