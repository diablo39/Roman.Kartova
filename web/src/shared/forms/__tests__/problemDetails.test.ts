import { describe, it, expect, vi } from "vitest";
import { applyProblemDetailsToForm, type ProblemDetails } from "../problemDetails";

describe("applyProblemDetailsToForm", () => {
  it("calls setError per field/message pair", () => {
    const setError = vi.fn();
    const payload: ProblemDetails = {
      type: "about:blank",
      title: "Validation failed",
      status: 400,
      errors: {
        name: ["Name is required"],
        displayName: ["Display name must be at most 128 chars"],
      },
    };

    const applied = applyProblemDetailsToForm(payload, setError);

    expect(applied).toBe(true);
    expect(setError).toHaveBeenCalledWith("name", { type: "server", message: "Name is required" });
    expect(setError).toHaveBeenCalledWith("displayName", {
      type: "server",
      message: "Display name must be at most 128 chars",
    });
    expect(setError).toHaveBeenCalledTimes(2);
  });

  it("calls setError once per message when a field has multiple", () => {
    const setError = vi.fn();
    applyProblemDetailsToForm(
      { status: 400, errors: { name: ["m1", "m2"] } },
      setError
    );
    expect(setError).toHaveBeenCalledTimes(2);
    expect(setError).toHaveBeenNthCalledWith(1, "name", { type: "server", message: "m1" });
    expect(setError).toHaveBeenNthCalledWith(2, "name", { type: "server", message: "m2" });
  });

  it("returns false and does not call setError when payload has no errors field", () => {
    const setError = vi.fn();
    const r = applyProblemDetailsToForm(
      { status: 400, title: "x" } as ProblemDetails,
      setError
    );
    expect(r).toBe(false);
    expect(setError).not.toHaveBeenCalled();
  });

  it("returns false when payload is null/undefined-ish", () => {
    const setError = vi.fn();
    expect(applyProblemDetailsToForm(null as unknown as ProblemDetails, setError)).toBe(false);
    expect(applyProblemDetailsToForm(undefined as unknown as ProblemDetails, setError)).toBe(false);
    expect(setError).not.toHaveBeenCalled();
  });

  it("ignores non-array values under errors", () => {
    const setError = vi.fn();
    const r = applyProblemDetailsToForm(
      {
        status: 400,
        errors: {
          // intentionally bad shape
          name: "not-an-array" as unknown as string[],
          displayName: ["valid"],
        },
      },
      setError
    );
    expect(r).toBe(true);
    // Only the valid one fired.
    expect(setError).toHaveBeenCalledTimes(1);
    expect(setError).toHaveBeenCalledWith("displayName", { type: "server", message: "valid" });
  });
});
