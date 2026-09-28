import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Drive react-oidc-context's useAuth from a mutable test value (mirrors
// web/src/app/__tests__/providers.test.tsx).
const signinRedirect = vi.fn();
let authValue: {
  isAuthenticated: boolean;
  isLoading: boolean;
  user?: { access_token: string };
  signinRedirect: typeof signinRedirect;
  error?: { source: string; message: string };
};
vi.mock("react-oidc-context", () => ({
  useAuth: () => authValue,
}));

import { useApiAuthBridge } from "@/shared/oidc/useApiAuthBridge";

function authedAuth(token = "tok-live") {
  return { isAuthenticated: true, isLoading: false, user: { access_token: token }, signinRedirect };
}

function TestHost({
  setTokenProvider,
  setUnauthorizedHandler,
}: {
  setTokenProvider: (p: () => string | null) => void;
  setUnauthorizedHandler: (h: () => void) => void;
}) {
  useApiAuthBridge(setTokenProvider, setUnauthorizedHandler);
  return null;
}

beforeEach(() => {
  signinRedirect.mockClear();
  signinRedirect.mockReset();
});

afterEach(() => {
  window.history.pushState({}, "", "/");
  vi.restoreAllMocks();
});

describe("useApiAuthBridge", () => {
  it("guards re-entrancy: two consecutive 401-handler calls fire signinRedirect only once", () => {
    signinRedirect.mockReturnValue(new Promise(() => {})); // in-flight redirect, never resolves
    const setTokenProvider = vi.fn();
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);

    const handler = setUnauthorizedHandler.mock.calls.at(-1)![0];
    handler();
    handler();

    expect(signinRedirect).toHaveBeenCalledTimes(1);
  });

  // react-oidc-context navigator methods never reject: a failed redirect resolves null and surfaces as
  // auth.error with source "signinRedirect" on the next render (react-oidc-context.js:170-190).
  it("failed redirect (resolves null + auth.error) releases the guard and logs, so a later 401 retries", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    signinRedirect.mockResolvedValue(null);
    const setTokenProvider = vi.fn();
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    const { rerender } = render(
      <TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />,
    );

    setUnauthorizedHandler.mock.calls.at(-1)![0]();
    expect(signinRedirect).toHaveBeenCalledTimes(1);

    const error = { source: "signinRedirect", message: "x" };
    authValue = { ...authedAuth(), error };
    rerender(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);
    expect(consoleError).toHaveBeenCalledWith("Re-authentication redirect failed:", error);

    setUnauthorizedHandler.mock.calls.at(-1)![0]();
    expect(signinRedirect).toHaveBeenCalledTimes(2);
  });

  it("an auth.error from another source (e.g. renewSilent) neither releases the guard nor logs", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    signinRedirect.mockReturnValue(new Promise(() => {}));
    const setTokenProvider = vi.fn();
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    const { rerender } = render(
      <TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />,
    );
    setUnauthorizedHandler.mock.calls.at(-1)![0]();

    authValue = { ...authedAuth(), error: { source: "renewSilent", message: "x" } };
    rerender(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);
    setUnauthorizedHandler.mock.calls.at(-1)![0]();

    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("the token provider yields the live token after an auth transition (loading → authenticated)", () => {
    authValue = { isAuthenticated: false, isLoading: true, signinRedirect };
    const setTokenProvider = vi.fn();
    const setUnauthorizedHandler = vi.fn();
    const { rerender } = render(
      <TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />,
    );
    const providerInstalledWhileLoading = setTokenProvider.mock.calls.at(-1)![0];

    authValue = authedAuth("tok-live");
    rerender(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);

    expect(providerInstalledWhileLoading()).toBe("tok-live");
  });
});
