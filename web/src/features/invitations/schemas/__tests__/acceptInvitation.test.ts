import { describe, it, expect } from "vitest";
import { acceptInvitationSchema } from "../acceptInvitation";

describe("acceptInvitationSchema", () => {
  const validInput = {
    password: "a".repeat(12),
    confirmPassword: "a".repeat(12),
    displayName: "Alice Smith",
  };

  it("accepts valid input (password 12, matching confirm, non-empty displayName)", () => {
    const r = acceptInvitationSchema.safeParse(validInput);
    expect(r.success).toBe(true);
  });

  it("rejects password length 11 (below minimum)", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      password: "a".repeat(11),
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /at least 12/i.test(i.message))).toBe(
        true
      );
    }
  });

  it("rejects password length 129 (above maximum)", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      password: "a".repeat(129),
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /too long/i.test(i.message))).toBe(
        true
      );
    }
  });

  it("rejects confirmPassword that does not match password", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      confirmPassword: "b".repeat(12),
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      const issue = r.error.issues.find((i) => i.path.includes("confirmPassword"));
      expect(issue).toBeDefined();
      expect(/do not match/i.test(issue?.message ?? "")).toBe(true);
    }
  });

  it("rejects empty displayName", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      displayName: "",
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /required/i.test(i.message))).toBe(
        true
      );
    }
  });

  it("rejects whitespace-only displayName", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      displayName: "   ",
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /required/i.test(i.message))).toBe(
        true
      );
    }
  });

  it("rejects displayName length 129 (above maximum)", () => {
    const r = acceptInvitationSchema.safeParse({
      ...validInput,
      displayName: "a".repeat(129),
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.some((i) => /too long/i.test(i.message))).toBe(
        true
      );
    }
  });
});
