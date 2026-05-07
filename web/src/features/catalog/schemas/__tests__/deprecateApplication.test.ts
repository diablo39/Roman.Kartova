import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { deprecateApplicationSchema } from "../deprecateApplication";

describe("deprecateApplicationSchema", () => {
  beforeEach(() => {
    vi.useFakeTimers().setSystemTime(new Date("2026-05-06T12:00:00Z"));
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("accepts a future ISO date", () => {
    expect(
      deprecateApplicationSchema.safeParse({ sunsetDate: "2026-12-31T00:00:00Z" }).success
    ).toBe(true);
  });

  it("rejects a past ISO date", () => {
    expect(
      deprecateApplicationSchema.safeParse({ sunsetDate: "2026-01-01T00:00:00Z" }).success
    ).toBe(false);
  });

  it("rejects an unparseable string", () => {
    expect(
      deprecateApplicationSchema.safeParse({ sunsetDate: "not-a-date" }).success
    ).toBe(false);
  });

  it("rejects an empty string", () => {
    expect(
      deprecateApplicationSchema.safeParse({ sunsetDate: "" }).success
    ).toBe(false);
  });
});
