import { describe, it, expect } from "vitest";
import { registerServiceSchema, endpointSchema, PROTOCOLS, PROTOCOL_LABEL } from "../registerService";

// Valid RFC-4122 UUID (version 4, variant 8) — zod v4 `.uuid()` enforces the
// version/variant bits, matching the registerApplication schema-test precedent.
const validTeamId = "a0000000-0000-4000-8000-000000000001";

describe("registerServiceSchema", () => {
  it("accepts a valid service with zero endpoints", () => {
    const r = registerServiceSchema.safeParse({
      displayName: "Orders", description: "Order service", teamId: validTeamId, endpoints: [],
    });
    expect(r.success).toBe(true);
  });

  it("accepts a valid service with endpoints", () => {
    const r = registerServiceSchema.safeParse({
      displayName: "Orders", description: "Order service", teamId: validTeamId,
      endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
    });
    expect(r.success).toBe(true);
  });

  it("rejects an empty display name", () => {
    const r = registerServiceSchema.safeParse({ displayName: "", description: "d", teamId: validTeamId, endpoints: [] });
    expect(r.success).toBe(false);
  });

  it("rejects a non-uuid team id", () => {
    const r = registerServiceSchema.safeParse({ displayName: "Orders", description: "d", teamId: "not-a-uuid", endpoints: [] });
    expect(r.success).toBe(false);
  });

  it("rejects more than 50 endpoints", () => {
    const endpoints = Array.from({ length: 51 }, () => ({ url: "https://x.example.com", protocol: "rest" as const }));
    const r = registerServiceSchema.safeParse({ displayName: "Orders", description: "d", teamId: validTeamId, endpoints });
    expect(r.success).toBe(false);
  });
});

describe("endpointSchema", () => {
  it("accepts an absolute https URL", () => {
    expect(endpointSchema.safeParse({ url: "https://api.example.com/v1", protocol: "rest" }).success).toBe(true);
  });
  it("accepts a grpc/tcp/ws scheme", () => {
    expect(endpointSchema.safeParse({ url: "grpc://svc.internal:50051", protocol: "grpc" }).success).toBe(true);
  });
  it("rejects an empty url", () => {
    expect(endpointSchema.safeParse({ url: "", protocol: "rest" }).success).toBe(false);
  });
  it("rejects a relative url", () => {
    expect(endpointSchema.safeParse({ url: "/v1/orders", protocol: "rest" }).success).toBe(false);
  });
  it("rejects an unknown protocol", () => {
    expect(endpointSchema.safeParse({ url: "https://x.example.com", protocol: "soap" }).success).toBe(false);
  });
  it("rejects a url longer than 2048 characters", () => {
    const longUrl = "https://x.example.com/" + "a".repeat(2040);
    expect(endpointSchema.safeParse({ url: longUrl, protocol: "rest" }).success).toBe(false);
  });
});

describe("PROTOCOLS / PROTOCOL_LABEL", () => {
  it("exposes all six protocols with labels", () => {
    expect(PROTOCOLS).toHaveLength(6);
    for (const p of PROTOCOLS) expect(typeof PROTOCOL_LABEL[p]).toBe("string");
  });
});
