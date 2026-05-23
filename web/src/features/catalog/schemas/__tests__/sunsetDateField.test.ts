import { describe, it, expect, vi } from "vitest";
import { sunsetDateField } from "../sunsetDateField";

describe("sunsetDateField", () => {
  it("accepts a strictly-future ISO date string", () => {
    const future = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
    expect(sunsetDateField.safeParse(future).success).toBe(true);
  });

  it("rejects a past ISO date string", () => {
    const past = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    const result = sunsetDateField.safeParse(past);
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.length).toBeGreaterThan(0);
      expect(result.error.issues[0]!.message).toMatch(/in the future/i);
    }
  });

  it("rejects today (now-boundary) as not strictly future", () => {
    // Mock Date.now to a fixed point so the test is deterministic.
    const fixedNow = new Date("2026-05-22T12:00:00Z").getTime();
    vi.spyOn(Date, "now").mockReturnValue(fixedNow);
    const sameAsNow = new Date(fixedNow).toISOString();
    expect(sunsetDateField.safeParse(sameAsNow).success).toBe(false);
    vi.restoreAllMocks();
  });

  it("rejects an empty string", () => {
    const result = sunsetDateField.safeParse("");
    expect(result.success).toBe(false);
  });

  it("rejects an invalid date string", () => {
    const result = sunsetDateField.safeParse("not-a-date");
    expect(result.success).toBe(false);
  });
});
