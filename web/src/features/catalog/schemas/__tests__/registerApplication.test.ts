import { describe, it, expect } from "vitest";
import { registerApplicationSchema } from "../registerApplication";

const validInput = {
  displayName: "Payment Gateway",
  description: "Handles charges",
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
    expect(registerApplicationSchema.safeParse({ displayName: "X" }).success).toBe(false);
  });

  it("rejects description over 4096 chars", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, description: "y".repeat(4097) }).success).toBe(false);
  });
});
