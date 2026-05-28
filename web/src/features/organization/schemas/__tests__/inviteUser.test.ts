import { describe, it, expect } from "vitest";
import {
  inviteUserSchema,
  KARTOVA_ROLES,
  type KartovaRole,
} from "../inviteUser";

describe("inviteUserSchema", () => {
  const valid = { email: "alice@example.com", role: "Member" as KartovaRole };

  it("accepts a valid email + role", () => {
    const r = inviteUserSchema.safeParse(valid);
    expect(r.success).toBe(true);
  });

  it("rejects an empty email with 'required'", () => {
    const r = inviteUserSchema.safeParse({ ...valid, email: "" });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /required/i.test(i.message))).toBe(true);
    }
  });

  it("rejects an invalid email format", () => {
    const r = inviteUserSchema.safeParse({ ...valid, email: "not-an-email" });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /invalid email/i.test(i.message))).toBe(true);
    }
  });

  it("rejects an email longer than 320 chars", () => {
    // Build a 321-character valid-shape email so we hit the `.max(320)` rule
    // rather than the format rule.
    const local = "a".repeat(321 - "@example.com".length);
    const longEmail = `${local}@example.com`;
    expect(longEmail.length).toBe(321);
    const r = inviteUserSchema.safeParse({ ...valid, email: longEmail });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /too long/i.test(i.message))).toBe(true);
    }
  });

  it("rejects an unknown role", () => {
    const r = inviteUserSchema.safeParse({ ...valid, role: "RootGod" });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /pick a role/i.test(i.message))).toBe(true);
    }
  });

  it.each(KARTOVA_ROLES)("accepts the %s role", (role) => {
    const r = inviteUserSchema.safeParse({ email: "alice@example.com", role });
    expect(r.success).toBe(true);
  });
});
