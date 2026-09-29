import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { setAdminAccessTokenProvider, setAdminUnauthorizedHandler } = vi.hoisted(() => ({
  setAdminAccessTokenProvider: vi.fn<(p: () => string | null) => void>(),
  setAdminUnauthorizedHandler: vi.fn<(h: () => void) => void>(),
}));
vi.mock("@/admin/api/client", async (orig) => {
  const actual = await orig<typeof import("@/admin/api/client")>();
  return { ...actual, setAdminAccessTokenProvider, setAdminUnauthorizedHandler };
});

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

import { AdminApiAuthBridge } from "@/admin/providers";

beforeEach(() => {
  setAdminAccessTokenProvider.mockClear();
  setAdminUnauthorizedHandler.mockClear();
  signinRedirect.mockClear();
});
afterEach(() => window.history.pushState({}, "", "/"));

describe("AdminApiAuthBridge", () => {
  it("the 401 handler re-authenticates with the current deep link as returnTo", () => {
    window.history.pushState({}, "", "/organizations?q=acme#top");
    authValue = { isAuthenticated: true, isLoading: false, user: { access_token: "t" }, signinRedirect };
    render(<AdminApiAuthBridge>x</AdminApiAuthBridge>);

    setAdminUnauthorizedHandler.mock.calls.at(-1)![0]();

    expect(signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/organizations?q=acme#top" } });
  });

  it("the token provider yields the live token even when installed before auth resolved", () => {
    authValue = { isAuthenticated: false, isLoading: true, signinRedirect };
    const { rerender } = render(<AdminApiAuthBridge>x</AdminApiAuthBridge>);
    const provider = setAdminAccessTokenProvider.mock.calls.at(-1)![0];

    authValue = { isAuthenticated: true, isLoading: false, user: { access_token: "tok-live" }, signinRedirect };
    rerender(<AdminApiAuthBridge>x</AdminApiAuthBridge>);

    expect(provider()).toBe("tok-live");
  });
});
