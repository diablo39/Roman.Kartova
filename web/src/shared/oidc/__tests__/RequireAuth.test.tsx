import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

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
});
