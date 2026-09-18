import { describe, it, expect } from "vitest";
import { registerEnvironmentSchema } from "../registerEnvironment";

describe("registerEnvironmentSchema", () => {
  it("accepts a valid environment", () => {
    const r = registerEnvironmentSchema.safeParse({
      displayName: "Prod EU", description: "primary", type: "production", region: "eu", cluster: "c1",
    });
    expect(r.success).toBe(true);
  });
  it("rejects a blank name", () => {
    expect(registerEnvironmentSchema.safeParse({ displayName: "", description: "d", type: "development" }).success).toBe(false);
  });
  it("rejects an unknown type", () => {
    expect(registerEnvironmentSchema.safeParse({ displayName: "x", description: "d", type: "qa" }).success).toBe(false);
  });
});
