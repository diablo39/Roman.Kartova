import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

// ---------- Mocks (hoisted) ----------

const navigateMock = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>(
    "react-router-dom",
  );
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

// Stub the handler so we don't drag in its mutate / queryClient pipeline.
vi.mock("@/features/auth/components/OidcCallbackHandler", () => ({
  OidcCallbackHandler: () => <div data-testid="oidc-callback-handler" />,
}));

import { CallbackPage } from "../CallbackPage";
import { resolveReturnTo } from "@/shared/auth/returnTo";

function renderPage() {
  return render(
    <MemoryRouter>
      <CallbackPage />
    </MemoryRouter>,
  );
}

describe("CallbackPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the loading spinner while the OIDC token exchange is in flight", () => {
    useAuthMock.mockReturnValue({
      isLoading: true,
      isAuthenticated: false,
      error: undefined,
    });
    renderPage();
    expect(screen.getByRole("status")).toHaveAttribute(
      "aria-label",
      "Completing sign-in…",
    );
    expect(screen.queryByTestId("oidc-callback-handler")).toBeNull();
  });

  it("hands off to OidcCallbackHandler once the OIDC user is authenticated", () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: true,
      error: undefined,
    });
    renderPage();
    expect(screen.getByTestId("oidc-callback-handler")).toBeInTheDocument();
  });

  it("navigates to /login-error and shows a redirect spinner when auth.error is set", async () => {
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: false,
      error: { message: "boom" },
    });
    renderPage();
    expect(screen.getByRole("status")).toHaveAttribute(
      "aria-label",
      "Sign-in failed; redirecting…",
    );
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/login-error", {
        replace: true,
      }),
    );
  });
});

describe("resolveReturnTo", () => {
  it("returns a same-origin relative path with its query", () => {
    expect(resolveReturnTo({ returnTo: "/catalog/services?displayNameContains=foo" })).toBe(
      "/catalog/services?displayNameContains=foo",
    );
  });

  it("rejects protocol-relative URLs (open-redirect guard)", () => {
    expect(resolveReturnTo({ returnTo: "//evil.example.com/phish" })).toBeUndefined();
  });

  it("rejects absolute URLs", () => {
    expect(resolveReturnTo({ returnTo: "https://evil.example.com" })).toBeUndefined();
  });

  it("rejects the auth-flow routes so it never bounces back into login", () => {
    expect(resolveReturnTo({ returnTo: "/callback?code=x" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/login-error" })).toBeUndefined();
    expect(resolveReturnTo({ returnTo: "/welcome" })).toBeUndefined();
  });

  it("returns undefined for missing / non-string / wrong-shaped state", () => {
    expect(resolveReturnTo(undefined)).toBeUndefined();
    expect(resolveReturnTo({})).toBeUndefined();
    expect(resolveReturnTo({ returnTo: 42 })).toBeUndefined();
    expect(resolveReturnTo("not-an-object")).toBeUndefined();
  });
});
