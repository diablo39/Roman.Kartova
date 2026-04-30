import { describe, it, expect } from "vitest";
import { registerApplicationSchema } from "../registerApplication";

const validInput = {
  name: "payment-gateway",
  displayName: "Payment Gateway",
  description: "Handles charges",
};

describe("registerApplicationSchema", () => {
  it("accepts a valid payload", () => {
    expect(registerApplicationSchema.safeParse(validInput).success).toBe(true);
  });

  it("requires name", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "" }).success).toBe(false);
  });

  it("rejects non-kebab-case name (PascalCase)", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "PaymentGateway" }).success).toBe(false);
  });

  it("rejects non-kebab-case name (snake_case)", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "payment_gateway" }).success).toBe(false);
  });

  it("rejects name starting with a digit", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "1payment" }).success).toBe(false);
  });

  it("accepts name with digits after first char", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "payment-v2" }).success).toBe(true);
  });

  it("rejects name longer than 64 chars", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, name: "a-" + "b".repeat(64) }).success).toBe(false);
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
    expect(registerApplicationSchema.safeParse({ name: "x", displayName: "X" }).success).toBe(false);
  });

  it("rejects description over 512 chars", () => {
    expect(registerApplicationSchema.safeParse({ ...validInput, description: "y".repeat(513) }).success).toBe(false);
  });
});
