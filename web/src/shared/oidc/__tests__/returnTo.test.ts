import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { resolveReturnTo } from "../returnTo";

describe("resolveReturnTo", () => {
  let warn: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    warn = vi.spyOn(console, "warn").mockImplementation(() => {});
  });
  afterEach(() => {
    warn.mockRestore();
  });

  it("returns a same-origin relative path with its query", () => {
    expect(resolveReturnTo({ returnTo: "/catalog/services?displayNameContains=foo" })).toBe(
      "/catalog/services?displayNameContains=foo",
    );
    expect(warn).not.toHaveBeenCalled();
  });

  it("preserves a hash fragment", () => {
    expect(resolveReturnTo({ returnTo: "/catalog/services?q=1#section" })).toBe(
      "/catalog/services?q=1#section",
    );
  });

  it("accepts the bare root path", () => {
    expect(resolveReturnTo({ returnTo: "/" })).toBe("/");
  });

  it("accepts paths that merely share a prefix with an auth route", () => {
    // Exact-path exclusion, not startsWith — these are legitimate destinations.
    expect(resolveReturnTo({ returnTo: "/welcome-back" })).toBe("/welcome-back");
    expect(resolveReturnTo({ returnTo: "/callbacks" })).toBe("/callbacks");
  });

  it("rejects protocol-relative URLs (open-redirect guard)", () => {
    expect(resolveReturnTo({ returnTo: "//evil.example.com/phish" })).toBeUndefined();
    expect(warn).toHaveBeenCalled();
  });

  it("rejects the backslash open-redirect bypass (\\ is treated as / by the URL parser)", () => {
    // `/\evil.com` and `/\/evil` resolve cross-origin — must be rejected.
    expect(resolveReturnTo({ returnTo: "/\\evil.example.com/phish" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/\\/evil.example.com" })).toBeUndefined();
  });

  it("rejects absolute URLs", () => {
    expect(resolveReturnTo({ returnTo: "https://evil.example.com" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "javascript:alert(1)" })).toBeUndefined();
  });

  it("rejects bare relative strings that do not start with /", () => {
    expect(resolveReturnTo({ returnTo: "catalog" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "" })).toBeUndefined();
  });

  it("rejects paths containing control chars (CR/LF/TAB)", () => {
    expect(resolveReturnTo({ returnTo: "/cata\tlog" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/catalog\n/evil" })).toBeUndefined();
  });

  it("rejects the auth-flow routes (case-insensitive, query/hash ignored)", () => {
    expect(resolveReturnTo({ returnTo: "/callback?code=x" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/login-error" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/welcome#x" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/accept-invitation?token=t" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/WELCOME" })).toBeUndefined();
  });

  it("returns undefined silently for absent state (no deep link to restore)", () => {
    expect(resolveReturnTo(undefined)).toBeUndefined();
    expect(resolveReturnTo({})).toBeUndefined();
    expect(warn).not.toHaveBeenCalled();
  });

  it("warns and returns undefined for present-but-non-string state", () => {
    expect(resolveReturnTo({ returnTo: 42 })).toBeUndefined();
    expect(resolveReturnTo("not-an-object")).toBeUndefined();
    expect(warn).toHaveBeenCalled();
  });
});
