import { describe, it, expect } from "vitest";
import { orgProfileSchema } from "../orgProfile";

describe("orgProfileSchema", () => {
  const validBase = {
    displayName: "Acme Corp",
    description: "Engineering org",
    defaultTimeZone: "Europe/Oslo",
  };

  describe("displayName", () => {
    it("accepts a normal 1-128 char name", () => {
      const r = orgProfileSchema.safeParse(validBase);
      expect(r.success).toBe(true);
    });

    it("rejects empty string", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, displayName: "" });
      expect(r.success).toBe(false);
      if (!r.success) {
        expect(r.error.issues.some((i) => /required/i.test(i.message))).toBe(true);
      }
    });

    it("rejects whitespace-only", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, displayName: "   " });
      expect(r.success).toBe(false);
      if (!r.success) {
        expect(r.error.issues.some((i) => /whitespace/i.test(i.message))).toBe(true);
      }
    });

    it("rejects > 128 chars", () => {
      const r = orgProfileSchema.safeParse({
        ...validBase,
        displayName: "a".repeat(129),
      });
      expect(r.success).toBe(false);
    });
  });

  describe("description", () => {
    it("accepts null", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, description: null });
      expect(r.success).toBe(true);
    });

    it("accepts empty string (API treats it as null)", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, description: "" });
      expect(r.success).toBe(true);
    });

    it("accepts a string up to 1024 chars", () => {
      const r = orgProfileSchema.safeParse({
        ...validBase,
        description: "x".repeat(1024),
      });
      expect(r.success).toBe(true);
    });

    it("rejects > 1024 chars", () => {
      const r = orgProfileSchema.safeParse({
        ...validBase,
        description: "x".repeat(1025),
      });
      expect(r.success).toBe(false);
    });

    it("accepts undefined (optional)", () => {
      const r = orgProfileSchema.safeParse({
        displayName: validBase.displayName,
        defaultTimeZone: validBase.defaultTimeZone,
      });
      expect(r.success).toBe(true);
    });
  });

  describe("defaultTimeZone", () => {
    it("accepts a runtime-known IANA zone", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, defaultTimeZone: "UTC" });
      expect(r.success).toBe(true);
    });

    it("rejects an unknown zone string", () => {
      const r = orgProfileSchema.safeParse({
        ...validBase,
        defaultTimeZone: "Mars/Olympus",
      });
      expect(r.success).toBe(false);
      if (!r.success) {
        expect(r.error.issues.some((i) => /Unknown IANA/.test(i.message))).toBe(true);
      }
    });

    it("rejects empty string", () => {
      const r = orgProfileSchema.safeParse({ ...validBase, defaultTimeZone: "" });
      expect(r.success).toBe(false);
    });
  });
});
