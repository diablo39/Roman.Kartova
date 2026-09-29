import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  REAUTH_LOOP_WINDOW_MS,
  clearReauthMarker,
  isRecentReauthAttempt,
  markReauthAttempt,
} from "../reauthMarker";

beforeEach(() => window.sessionStorage.clear());
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("reauthMarker", () => {
  it("is not recent when no attempt was marked", () => {
    expect(isRecentReauthAttempt(1_000)).toBe(false);
  });

  it("is recent inside the window and not at or after its end", () => {
    markReauthAttempt(10_000);
    expect(isRecentReauthAttempt(10_000)).toBe(true);
    expect(isRecentReauthAttempt(10_000 + REAUTH_LOOP_WINDOW_MS - 1)).toBe(true);
    expect(isRecentReauthAttempt(10_000 + REAUTH_LOOP_WINDOW_MS)).toBe(false);
  });

  it("is not recent when the marker lies in the future (clock moved back)", () => {
    markReauthAttempt(50_000);
    expect(isRecentReauthAttempt(49_999)).toBe(false);
  });

  it("defaults to Date.now()", () => {
    vi.spyOn(Date, "now").mockReturnValue(5_000);
    markReauthAttempt();
    vi.spyOn(Date, "now").mockReturnValue(5_000 + REAUTH_LOOP_WINDOW_MS - 1);
    expect(isRecentReauthAttempt()).toBe(true);
  });

  it("clearReauthMarker removes the attempt", () => {
    markReauthAttempt(1_000);
    clearReauthMarker();
    expect(isRecentReauthAttempt(1_000)).toBe(false);
  });

  it("ignores a malformed stored value", () => {
    window.sessionStorage.setItem("kartova.reauth-attempt", "not-json");
    expect(isRecentReauthAttempt(1_000)).toBe(false);
    window.sessionStorage.setItem("kartova.reauth-attempt", JSON.stringify({ ts: "1000" }));
    expect(isRecentReauthAttempt(1_000)).toBe(false);
  });

  it("treats unavailable storage as 'no marker' and never throws", () => {
    const mockStorage = {
      setItem: vi.fn(() => { throw new Error("denied"); }),
      getItem: vi.fn(() => { throw new Error("denied"); }),
      removeItem: vi.fn(() => { throw new Error("denied"); }),
      clear: vi.fn(),
      key: vi.fn(() => null),
      length: 0,
    };
    // stubGlobal instead of vi.spyOn(Storage.prototype, …): jsdom sessionStorage doesn't reliably route through Storage.prototype in vitest
    vi.stubGlobal("sessionStorage", mockStorage);
    expect(() => markReauthAttempt(1_000)).not.toThrow();
    expect(isRecentReauthAttempt(1_000)).toBe(false);
    expect(() => clearReauthMarker()).not.toThrow();
  });
});
