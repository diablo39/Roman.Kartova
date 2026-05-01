import { describe, it, expect } from "vitest";
import { initialsOf } from "../initials";

describe("initialsOf", () => {
  it("returns ? for null/undefined/empty", () => {
    expect(initialsOf(null)).toBe("?");
    expect(initialsOf(undefined)).toBe("?");
    expect(initialsOf("")).toBe("?");
    expect(initialsOf("   ")).toBe("?");
  });
  it("returns first letter for single-word names", () => {
    expect(initialsOf("Alice")).toBe("A");
  });
  it("returns first letters of first two words", () => {
    expect(initialsOf("Alice Admin")).toBe("AA");
    expect(initialsOf("Mary Jane Watson")).toBe("MJ");
  });
  it("uppercases", () => {
    expect(initialsOf("alice admin")).toBe("AA");
  });
});
