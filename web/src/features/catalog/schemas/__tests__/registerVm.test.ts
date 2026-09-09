import { describe, it, expect } from "vitest";
import { registerVmSchema, editVmSchema, vmAttributesSchema, POWER_STATES } from "../registerVm";

const validAttributes = {
  powerState: "running" as const,
  os: "ubuntu-22.04",
  hostname: "host-1",
  region: "eu-west-1",
  vcpu: "4",
  memoryGb: "16",
  ipAddresses: ["10.0.0.1", "10.0.0.2"],
};

const valid = {
  displayName: "vm-01",
  description: "seeded",
  teamId: "a0000000-0000-4000-8000-000000000001",
  attributes: validAttributes,
};

describe("registerVmSchema", () => {
  it("accepts a valid payload", () => {
    expect(registerVmSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts a valid payload with provider", () => {
    expect(registerVmSchema.safeParse({ ...valid, provider: "AWS" }).success).toBe(true);
  });

  it("accepts a valid payload omitting provider", () => {
    const { provider: _provider, ...withoutProvider } = { ...valid, provider: undefined };
    expect(registerVmSchema.safeParse(withoutProvider).success).toBe(true);
  });

  it("rejects a provider over 256 characters", () => {
    expect(registerVmSchema.safeParse({ ...valid, provider: "a".repeat(257) }).success).toBe(false);
  });

  it("exposes all three power states", () => {
    expect(POWER_STATES).toEqual(["running", "stopped", "suspended"]);
  });

  it("rejects an unknown power state", () => {
    expect(
      registerVmSchema.safeParse({ ...valid, attributes: { ...validAttributes, powerState: "paused" } }).success,
    ).toBe(false);
  });

  it("rejects a non-uuid teamId", () => {
    expect(registerVmSchema.safeParse({ ...valid, teamId: "nope" }).success).toBe(false);
  });

  it("rejects an empty displayName", () => {
    expect(registerVmSchema.safeParse({ ...valid, displayName: "" }).success).toBe(false);
  });
});

describe("editVmSchema", () => {
  const validEdit = { displayName: valid.displayName, description: valid.description, attributes: validAttributes };

  it("accepts a valid payload without teamId", () => {
    expect(editVmSchema.safeParse(validEdit).success).toBe(true);
  });

  it("accepts a valid payload with provider", () => {
    expect(editVmSchema.safeParse({ ...validEdit, provider: "Azure" }).success).toBe(true);
  });

  it("omitting provider still parses", () => {
    expect(editVmSchema.safeParse(validEdit).success).toBe(true);
  });

  it("rejects a provider over 256 characters", () => {
    expect(editVmSchema.safeParse({ ...validEdit, provider: "a".repeat(257) }).success).toBe(false);
  });
});

// isValidIpAddress is not individually exported — exercised via vmAttributesSchema's
// `ipAddresses` field (the array-level refine that calls it per element).
describe("vmAttributesSchema.ipAddresses (isValidIpAddress)", () => {
  it("accepts a valid IPv4 address", () => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, ipAddresses: ["10.0.0.1"] }).success).toBe(true);
  });

  it("accepts a valid IPv6 address", () => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, ipAddresses: ["2001:db8::1"] }).success).toBe(true);
  });

  it("rejects a garbage IP address", () => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, ipAddresses: ["not-an-ip"] }).success).toBe(false);
  });

  it("rejects an out-of-range IPv4 octet", () => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, ipAddresses: ["999.999.999.999"] }).success).toBe(false);
  });

  it("rejects an empty ipAddresses array", () => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, ipAddresses: [] }).success).toBe(false);
  });
});

// positiveIntStringSchema is not individually exported — exercised via the `vcpu`/`memoryGb`
// fields, both built from it.
describe("vmAttributesSchema.vcpu / memoryGb (positiveIntStringSchema)", () => {
  it.each(["1", "4"])("accepts %s", (value) => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, vcpu: value }).success).toBe(true);
    expect(vmAttributesSchema.safeParse({ ...validAttributes, memoryGb: value }).success).toBe(true);
  });

  it.each(["0", "-1", "1.5", ""])("rejects %s", (value) => {
    expect(vmAttributesSchema.safeParse({ ...validAttributes, vcpu: value }).success).toBe(false);
    expect(vmAttributesSchema.safeParse({ ...validAttributes, memoryGb: value }).success).toBe(false);
  });
});
