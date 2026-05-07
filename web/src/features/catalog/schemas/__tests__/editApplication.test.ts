import { describe, expect, it } from "vitest";
import { editApplicationSchema } from "../editApplication";

describe("editApplicationSchema", () => {
  it("accepts valid input", () => {
    expect(
      editApplicationSchema.safeParse({ displayName: "X", description: "Y" }).success
    ).toBe(true);
  });

  it("rejects empty displayName", () => {
    expect(
      editApplicationSchema.safeParse({ displayName: "", description: "Y" }).success
    ).toBe(false);
  });

  it("rejects whitespace-only displayName", () => {
    expect(
      editApplicationSchema.safeParse({ displayName: "   ", description: "Y" }).success
    ).toBe(false);
  });

  it("rejects displayName over 128 chars", () => {
    expect(
      editApplicationSchema.safeParse({
        displayName: "x".repeat(129),
        description: "Y",
      }).success
    ).toBe(false);
  });

  it("rejects empty description", () => {
    expect(
      editApplicationSchema.safeParse({ displayName: "X", description: "" }).success
    ).toBe(false);
  });

  it("rejects whitespace-only description", () => {
    expect(
      editApplicationSchema.safeParse({ displayName: "X", description: "  " }).success
    ).toBe(false);
  });
});
