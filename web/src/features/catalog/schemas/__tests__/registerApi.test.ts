import { describe, it, expect } from "vitest";
import { registerApiSchema, API_STYLES, API_STYLE_LABEL } from "../registerApi";

const valid = {
  displayName: "Orders API",
  description: "Order management",
  style: "rest" as const,
  version: "v1",
  specUrl: "https://example.com/openapi.json",
  teamId: "a0000000-0000-4000-8000-000000000001",
};

describe("registerApiSchema", () => {
  it("accepts a valid payload", () => {
    expect(registerApiSchema.safeParse(valid).success).toBe(true);
  });
  it("accepts an omitted/empty specUrl", () => {
    expect(registerApiSchema.safeParse({ ...valid, specUrl: "" }).success).toBe(true);
  });
  it("rejects a relative specUrl", () => {
    expect(registerApiSchema.safeParse({ ...valid, specUrl: "/openapi.json" }).success).toBe(false);
  });
  it("rejects an empty displayName", () => {
    expect(registerApiSchema.safeParse({ ...valid, displayName: "" }).success).toBe(false);
  });
  it("rejects an empty version", () => {
    expect(registerApiSchema.safeParse({ ...valid, version: "" }).success).toBe(false);
  });
  it("exposes all four styles with labels", () => {
    expect(API_STYLES).toEqual(["rest", "grpc", "graphQL", "asyncApi"]);
    expect(API_STYLE_LABEL.graphQL).toBe("GraphQL");
  });
  it("accepts asyncApi style", () => {
    expect(registerApiSchema.shape.style.safeParse("asyncApi").success).toBe(true);
    expect(API_STYLE_LABEL.asyncApi).toBe("AsyncAPI");
  });
});
