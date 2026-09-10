import { describe, expect, it } from "vitest";
import { z } from "zod";
import { zodFieldPaths } from "../zodFieldPaths";
import { registerVmSchema, editVmSchema } from "@/features/catalog/schemas/registerVm";

describe("zodFieldPaths", () => {
  it("enumerates flat object keys", () => {
    const paths = zodFieldPaths(z.object({ a: z.string(), b: z.number() }));
    expect(paths).toEqual(new Set(["a", "b"]));
  });

  it("enumerates nested object paths (parent and descendants)", () => {
    const paths = zodFieldPaths(
      z.object({ outer: z.object({ inner: z.string() }) }),
    );
    expect(paths.has("outer")).toBe(true);
    expect(paths.has("outer.inner")).toBe(true);
  });

  it("unwraps optional and default wrappers", () => {
    const paths = zodFieldPaths(
      z.object({ a: z.string().optional(), b: z.string().default("x") }),
    );
    expect(paths).toEqual(new Set(["a", "b"]));
  });

  it("treats arrays as leaf paths (no synthetic indices)", () => {
    const paths = zodFieldPaths(z.object({ tags: z.array(z.string()) }));
    expect(paths).toEqual(new Set(["tags"]));
  });

  it("covers the real registerVm schema including attributes.* paths", () => {
    const paths = zodFieldPaths(registerVmSchema);
    expect(paths.has("displayName")).toBe(true);
    expect(paths.has("teamId")).toBe(true);
    expect(paths.has("attributes")).toBe(true);
    expect(paths.has("attributes.os")).toBe(true);
    expect(paths.has("attributes.vcpu")).toBe(true);
    expect(paths.has("attributes.ipAddresses")).toBe(true);
  });

  it("editVm omits teamId (mirrors the schema)", () => {
    const paths = zodFieldPaths(editVmSchema);
    expect(paths.has("teamId")).toBe(false);
    expect(paths.has("attributes.os")).toBe(true);
  });

  it("returns an empty set for a non-object schema", () => {
    expect(zodFieldPaths(z.string())).toEqual(new Set());
  });
});
