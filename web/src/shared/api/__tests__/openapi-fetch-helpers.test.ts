import { describe, it, expect } from "vitest";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";

describe("throwWithStatus", () => {
  it("attaches __status to the error and re-throws it", () => {
    const error = { title: "Conflict" };
    expect(() => throwWithStatus(error, { status: 409 })).toThrowError(
      expect.objectContaining({ title: "Conflict", __status: 409 }),
    );
  });
});

describe("unwrapData", () => {
  it("returns data when present", () => {
    expect(unwrapData({ id: "a1" })).toEqual({ id: "a1" });
  });

  it("returns data when present even alongside a response argument", () => {
    expect(unwrapData({ id: "a1" }, new Response(null, { status: 200 }))).toEqual({ id: "a1" });
  });

  it("throws a plain, status-less Error when data is absent and no response is passed", () => {
    // No `response` in scope (e.g. call sites that never destructured it) — unchanged legacy
    // behaviour: a contract violation, not a real failure mode to branch on.
    expect(() => unwrapData(undefined)).toThrowError("API returned neither data nor error");
    try {
      unwrapData(undefined);
      expect.unreachable();
    } catch (err) {
      expect((err as { __status?: number }).__status).toBeUndefined();
    }
  });

  it("throws a plain, status-less Error when data is absent but response.ok is true", () => {
    // An unexpected empty *success* is still a contract violation upstream, not a failure mode.
    expect(() => unwrapData(undefined, new Response(null, { status: 204 }))).toThrowError(
      "API returned neither data nor error",
    );
  });

  it("regression (gate-8 finding): synthesizes __status from response.status when data is absent and response is not ok — the ASP.NET Results.Forbid() empty-body-403 case", () => {
    // This is the exact shape openapi-fetch resolves to for a body-less ForbidResult:
    // { data: undefined, error: undefined, response } with response.ok === false. Before the
    // fix this fell through to the generic "API returned neither data nor error" with no
    // __status attached, so a caller's `byStatus: { 403: ... }` toastProblem table could never
    // match it — the one scenario that dispatch entry exists to catch.
    let caught: unknown;
    try {
      unwrapData(undefined, new Response(null, { status: 403 }));
      expect.unreachable();
    } catch (err) {
      caught = err;
    }
    expect((caught as { __status?: number }).__status).toBe(403);
    expect((caught as { title?: string }).title).toBe("Request failed with status 403");
  });

  it("attaches the correct __status for a different non-ok status (502)", () => {
    let caught: unknown;
    try {
      unwrapData(undefined, new Response(null, { status: 502 }));
      expect.unreachable();
    } catch (err) {
      caught = err;
    }
    expect((caught as { __status?: number }).__status).toBe(502);
  });
});
