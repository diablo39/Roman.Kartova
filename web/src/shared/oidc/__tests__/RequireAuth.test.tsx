import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const signinRedirect = vi.fn();
const useAuthMock = vi.fn();

vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { RequireAuth } from "../RequireAuth";

describe("RequireAuth", () => {
  beforeEach(() => {
    useAuthMock.mockReset();
    signinRedirect.mockReset();
  });

  it("renders fallback while loading", () => {
    useAuthMock.mockReturnValue({
      isLoading: true,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(screen.getByText(/signing in/i)).toBeInTheDocument();
    expect(screen.queryByText("protected")).not.toBeInTheDocument();
    expect(signinRedirect).not.toHaveBeenCalled();
  });

  it("triggers signinRedirect when unauthenticated and not loading", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(signinRedirect).toHaveBeenCalledTimes(1);
  });

  it("captures the current deep link (path + query + hash) in the OIDC state.returnTo", () => {
    window.history.pushState({}, "", "/catalog/services?displayNameContains=foo#sec");
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(signinRedirect).toHaveBeenCalledWith({
      state: { returnTo: "/catalog/services?displayNameContains=foo#sec" },
    });
    window.history.pushState({}, "", "/");
  });

  it("does not trigger signinRedirect when an active navigator is in flight", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: "signinRedirect",
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(signinRedirect).not.toHaveBeenCalled();
  });

  it("renders children when authenticated", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: true,
      signinRedirect,
      activeNavigator: undefined,
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(screen.getByText("protected")).toBeInTheDocument();
    expect(signinRedirect).not.toHaveBeenCalled();
  });

  // A failed signinRedirect (e.g. KeyCloak unreachable) resolves null and sets auth.error, then
  // NAVIGATOR_CLOSE clears activeNavigator — auto-redirecting again would loop without bound.
  it("does not re-redirect after a signinRedirect error; renders the retry panel", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
      error: { source: "signinRedirect", message: "Failed to fetch" },
    });
    const { rerender } = render(<RequireAuth><div>protected</div></RequireAuth>);
    rerender(<RequireAuth><div>protected</div></RequireAuth>);

    expect(signinRedirect).not.toHaveBeenCalled();
    expect(screen.getByRole("heading", { name: "Sign-in unavailable" })).toBeInTheDocument();
    expect(screen.getByText("The sign-in service could not be reached.")).toBeInTheDocument();
    expect(screen.queryByText("protected")).not.toBeInTheDocument();
  });

  it("Try again on the retry panel calls signinRedirect once with the deep link", async () => {
    window.history.pushState({}, "", "/organizations?q=acme#top");
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
      error: { source: "signinRedirect", message: "Failed to fetch" },
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);

    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(signinRedirect).toHaveBeenCalledWith({ state: { returnTo: "/organizations?q=acme#top" } });
    window.history.pushState({}, "", "/");
  });

  // LoginErrorPage's "Go home" relies on RequireAuth turning `/` into a fresh redirect while the
  // failed callback's auth.error (source signinCallback) is still in state — only a failed
  // signinRedirect stops the auto-redirect.
  it("still auto-redirects when auth.error comes from another source (signinCallback)", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      signinRedirect,
      activeNavigator: undefined,
      error: { source: "signinCallback", message: "state mismatch" },
    });
    render(<RequireAuth><div>protected</div></RequireAuth>);
    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("heading", { name: "Sign-in unavailable" })).toBeNull();
  });
});
