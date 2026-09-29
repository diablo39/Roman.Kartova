import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { markReauthAttempt } from "@/shared/oidc/reauthMarker";

const { setAdminAccessTokenProvider, setAdminUnauthorizedHandler } = vi.hoisted(() => ({
  setAdminAccessTokenProvider: vi.fn<(p: () => string | null) => void>(),
  setAdminUnauthorizedHandler: vi.fn<(h: (ctx: { hadToken: boolean }) => void) => void>(),
}));
vi.mock("@/admin/api/client", async (orig) => {
  const actual = await orig<typeof import("@/admin/api/client")>();
  return { ...actual, setAdminAccessTokenProvider, setAdminUnauthorizedHandler };
});

const signinRedirect = vi.fn();
const signoutRedirect = vi.fn();
let authValue: {
  isAuthenticated: boolean;
  isLoading: boolean;
  user?: { access_token: string };
  signinRedirect: typeof signinRedirect;
  signoutRedirect?: typeof signoutRedirect;
};
vi.mock("react-oidc-context", () => ({
  useAuth: () => authValue,
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
}));

import { AdminApiAuthBridge } from "@/admin/providers";

beforeEach(() => {
  window.sessionStorage.clear();
  setAdminAccessTokenProvider.mockClear();
  setAdminUnauthorizedHandler.mockClear();
  signinRedirect.mockClear();
  signoutRedirect.mockClear();
});
afterEach(() => {
  window.history.pushState({}, "", "/");
  vi.restoreAllMocks();
});

describe("AdminApiAuthBridge", () => {
  it("the 401 handler re-authenticates with the current deep link as returnTo", () => {
    window.history.pushState({}, "", "/organizations?q=acme#top");
    authValue = { isAuthenticated: true, isLoading: false, user: { access_token: "t" }, signinRedirect };
    render(<AdminApiAuthBridge>x</AdminApiAuthBridge>);

    setAdminUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true });

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

  it("renders the session-rejected panel instead of the console when the breaker trips", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    markReauthAttempt();
    authValue = { isAuthenticated: true, isLoading: false, user: { access_token: "t" }, signinRedirect, signoutRedirect };
    render(<AdminApiAuthBridge>console-content</AdminApiAuthBridge>);

    act(() => setAdminUnauthorizedHandler.mock.calls.at(-1)![0]({ hadToken: true }));

    expect(screen.getByRole("heading", { name: "Signed in, but the session was rejected" })).toBeInTheDocument();
    expect(screen.queryByText("console-content")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
    expect(signoutRedirect).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(screen.getByText("console-content")).toBeInTheDocument();
  });
});
