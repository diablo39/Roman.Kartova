import { describe, it, expect } from "vitest";
import { registerApplicationSchema } from "../registerApplication";

const VALID_TEAM_ID = "a0000000-0000-4000-8000-000000000001";

const validInput = {
  displayName: "Payment Gateway",
  description: "Handles charges",
  teamId: VALID_TEAM_ID,
};

describe("registerApplicationSchema", () => {
  it("accepts a valid payload", () => {
    expect(registerApplicationSchema.safeParse(validInput).success).toBe(true);
  });

  it("requires displayName", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, displayName: "" }).success).toBe(false);
  });

  it("rejects displayName over 128 chars", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, displayName: "x".repeat(129) }).success).toBe(false);
  });

  it("rejects empty description", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, description: "" }).success).toBe(false);
  });

  it("rejects missing description", () => {
    expect(registerApplicationSchema.safeParse({ displayName: "X", teamId: VALID_TEAM_ID }).success).toBe(false);
  });

  it("rejects description over 4096 chars", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, description: "y".repeat(4097) }).success).toBe(false);
  });

  it("requires teamId", () => {
    const { teamId: _, ...withoutTeam } = validInput;
    expect(registerApplicationSchema.safeParse(withoutTeam).success).toBe(false);
  });

  it("rejects a non-UUID teamId", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, teamId: "not-a-uuid" }).success).toBe(false);
  });

  it("rejects an empty teamId", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, teamId: "" }).success).toBe(false);
  });
});
