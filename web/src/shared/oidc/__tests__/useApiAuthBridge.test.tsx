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

  it("logs and releases the guard when signinRedirect rejects, so a later 401 retries", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    const failure = new Error("redirect failed");
    signinRedirect.mockRejectedValueOnce(failure);
    const setTokenProvider = vi.fn();
    const setUnauthorizedHandler = vi.fn();
    authValue = authedAuth();
    render(<TestHost setTokenProvider={setTokenProvider} setUnauthorizedHandler={setUnauthorizedHandler} />);

    const handler = setUnauthorizedHandler.mock.calls.at(-1)![0];
    handler();

    // Let the rejected promise's .catch handler run before asserting.
    await Promise.resolve();
    await Promise.resolve();
    expect(consoleError).toHaveBeenCalledWith("Re-authentication redirect failed:", failure);

    signinRedirect.mockResolvedValueOnce(undefined);
    handler();
    expect(signinRedirect).toHaveBeenCalledTimes(2);
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
