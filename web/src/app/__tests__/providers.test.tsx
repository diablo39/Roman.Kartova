import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Capture the provider/handler the bridge installs on the API client.
// vi.hoisted so the spies exist when the (hoisted) vi.mock factory runs.
const { setAccessTokenProvider, setUnauthorizedHandler } = vi.hoisted(() => ({
  setAccessTokenProvider: vi.fn<(p: () => string | null) => void>(),
  setUnauthorizedHandler: vi.fn<(h: () => void) => void>(),
}));
vi.mock("@/features/catalog/api/client", async (orig) => {
  const actual = await orig<typeof import("@/features/catalog/api/client")>();
  return { ...actual, setAccessTokenProvider, setUnauthorizedHandler };
});

// Drive react-oidc-context's useAuth from a mutable test value.
const signinRedirect = vi.fn();
let authValue: {
  isAuthenticated: boolean;
  isLoading: boolean;
  user?: { access_token: string };
  signinRedirect: typeof signinRedirect;
};
vi.mock("react-oidc-context", () => ({
  useAuth: () => authValue,
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
}));

import { ApiAuthBridge } from "@/app/providers";

function authedAuth(token = "tok-live") {
  return { isAuthenticated: true, isLoading: false, user: { access_token: token }, signinRedirect };
}

beforeEach(() => {
  setAccessTokenProvider.mockClear();
  setUnauthorizedHandler.mockClear();
  signinRedirect.mockClear();
});

afterEach(() => {
  window.history.pushState({}, "", "/");
});

describe("ApiAuthBridge", () => {
  it("the 401 handler preserves the current deep link as returnTo (no bounce to /catalog)", () => {
    window.history.pushState({}, "", "/catalog/applications/abc-123?tab=deps#graph");
    authValue = authedAuth();
    render(<ApiAuthBridge>x</ApiAuthBridge>);

    const handler = setUnauthorizedHandler.mock.calls.at(-1)![0];
    handler();

    expect(signinRedirect).toHaveBeenCalledWith({
      state: { returnTo: "/catalog/applications/abc-123?tab=deps#graph" },
    });
  });

  it("the token provider yields the live token even when installed before auth resolved", () => {
    // Installed during the OIDC loading phase (user not yet present)…
    authValue = { isAuthenticated: false, isLoading: true, signinRedirect };
    const { rerender } = render(<ApiAuthBridge>x</ApiAuthBridge>);
    const providerInstalledWhileLoading = setAccessTokenProvider.mock.calls.at(-1)![0];

    // …then auth resolves on a later render. The provider captured during the
    // loading phase must reflect the now-live token, not the stale null snapshot
    // (otherwise the first authed request races out tokenless → 401 → bounce).
    authValue = authedAuth("tok-live");
    rerender(<ApiAuthBridge>x</ApiAuthBridge>);

    expect(providerInstalledWhileLoading()).toBe("tok-live");
  });

  it("the 401 handler uses the live signinRedirect even when installed before auth resolved", () => {
    const signinRedirectOld = vi.fn();
    authValue = { isAuthenticated: false, isLoading: true, signinRedirect: signinRedirectOld };
    const { rerender } = render(<ApiAuthBridge>x</ApiAuthBridge>);
    const handlerInstalledWhileLoading = setUnauthorizedHandler.mock.calls.at(-1)![0];

    const signinRedirectNew = vi.fn();
    authValue = {
      isAuthenticated: true,
      isLoading: false,
      user: { access_token: "t" },
      signinRedirect: signinRedirectNew,
    };
    rerender(<ApiAuthBridge>x</ApiAuthBridge>);

    handlerInstalledWhileLoading();
    expect(signinRedirectNew).toHaveBeenCalledTimes(1);
    expect(signinRedirectOld).not.toHaveBeenCalled();
  });
});
