import { act, render } from "@testing-library/react";
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
import { REAUTH_LOOP_WINDOW_MS, isRecentReauthAttempt, markReauthAttempt } from "@/shared/oidc/reauthMarker";

function authedAuth(token = "tok-live") {
  return { isAuthenticated: true, isLoading: false, user: { access_token: token }, signinRedirect };
}

let bridge: ReturnType<typeof useApiAuthBridge> | undefined;

function TestHost({
  setTokenProvider,
  setUnauthorizedHandler,
}: {
  setTokenProvider: (p: () => string | null) => void;
  setUnauthorizedHandler: (h: (ctx: { hadToken: boolean }) => void) => void;
}) {
  bridge = useApiAuthBridge(setTokenProvider, setUnauthorizedHandler);
  return null;
}

beforeEach(() => {
  window.sessionStorage.clear();
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
    handler({ hadToken: true });
    handler({ hadToken: true });

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

    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });
    expect(signinRedirect).toHaveBeenCalledTimes(1);

    const error = { source: "signinRedirect", message: "x" };
    authValue = { ...authedAuth(), error };
    rerender(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);
    expect(consoleError).toHaveBeenCalledWith("Re-authentication redirect failed:", error);

    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });
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
    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });

    authValue = { ...authedAuth(), error: { source: "renewSilent", message: "x" } };
    rerender(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);
    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });

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

  it("a 401 marks the attempt before redirecting", () => {
    signinRedirect.mockReturnValue(new Promise(() => {}));
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);

    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });

    expect(isRecentReauthAttempt()).toBe(true);
    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(bridge!.reauthFailed).toBe(false);
  });

  it("a 401 right after a marked re-auth (SSO returned, API still rejects) trips the breaker instead of redirecting", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    markReauthAttempt(); // the previous page load redirected moments ago
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);

    act(() => setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true }));

    expect(signinRedirect).not.toHaveBeenCalled();
    expect(bridge!.reauthFailed).toBe(true);
    expect(consoleError).toHaveBeenCalled();
  });

  it("a tokenless 401 never trips the breaker, even right after a marked re-auth", () => {
    markReauthAttempt(); // the previous page load redirected moments ago
    signinRedirect.mockReturnValue(new Promise(() => {}));
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);

    act(() => setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: false }));

    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(bridge!.reauthFailed).toBe(false);
  });

  it("a stale marker (older than the window, e.g. slow password entry) redirects and re-marks", () => {
    vi.spyOn(Date, "now").mockReturnValue(100_000);
    markReauthAttempt(100_000 - REAUTH_LOOP_WINDOW_MS);
    signinRedirect.mockReturnValue(new Promise(() => {}));
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);

    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });

    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(bridge!.reauthFailed).toBe(false);
    expect(isRecentReauthAttempt(100_000)).toBe(true);
  });

  it("a failed redirect (auth.error signinRedirect) clears the marker, so the next 401 retries rather than tripping", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    signinRedirect.mockResolvedValue(null);
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    const { rerender } = render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);
    setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });
    expect(isRecentReauthAttempt()).toBe(true);

    authValue = { ...authedAuth(), error: { source: "signinRedirect", message: "x" } };
    rerender(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);

    expect(isRecentReauthAttempt()).toBe(false);
    act(() => setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true }));
    expect(signinRedirect).toHaveBeenCalledTimes(2);
    expect(bridge!.reauthFailed).toBe(false);
  });

  it("retry clears the tripped state, re-marks, and redirects with the current deep link", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    window.history.pushState({}, "", "/catalog/services?q=x#top");
    markReauthAttempt();
    signinRedirect.mockReturnValue(new Promise(() => {}));
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={vi.fn()} setUnauthorizedHandler={setUnauthorizedHandler} />);
    act(() => setUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true }));
    expect(bridge!.reauthFailed).toBe(true);

    act(() => bridge!.retry());

    expect(bridge!.reauthFailed).toBe(false);
    expect(isRecentReauthAttempt()).toBe(true);
    expect(signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/catalog/services?q=x#top" } });
  });
});
