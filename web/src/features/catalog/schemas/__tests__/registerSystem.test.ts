import { describe, it, expect } from "vitest";
import { registerSystemSchema } from "../registerSystem";

describe("registerSystemSchema", () => {
  const base = { displayName: "Payments Platform", description: "Core", teamId: "a0000000-0000-4000-8000-000000000001" };

  it("accepts a valid input", () => {
    expect(registerSystemSchema.safeParse(base).success).toBe(true);
  });

  it("accepts a missing/empty description (optional)", () => {
    expect(registerSystemSchema.safeParse({ ...base, description: "" }).success).toBe(true);
    const { description, ...noDesc } = base;
    expect(registerSystemSchema.safeParse(noDesc).success).toBe(true);
  });

  it("rejects an empty display name", () => {
    expect(registerSystemSchema.safeParse({ ...base, displayName: "" }).success).toBe(false);
  });

  it("rejects a display name over 128 chars", () => {
    expect(registerSystemSchema.safeParse({ ...base, displayName: "x".repeat(129) }).success).toBe(false);
  });

  it("rejects a description over 4096 chars", () => {
    expect(registerSystemSchema.safeParse({ ...base, description: "x".repeat(4097) }).success).toBe(false);
  });

  it("rejects a non-uuid teamId", () => {
    expect(registerSystemSchema.safeParse({ ...base, teamId: "nope" }).success).toBe(false);
    // Zod v4's .uuid() enforces RFC-4122 version/variant bits — an all-1s value is malformed.
    expect(registerSystemSchema.safeParse({ ...base, teamId: "11111111-1111-1111-1111-111111111111" }).success).toBe(false);
  });
});
