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
// Surface the `returnTo` prop so we can assert CallbackPage's wiring.
vi.mock("@/features/auth/components/OidcCallbackHandler", () => ({
  OidcCallbackHandler: ({ returnTo }: { returnTo?: string }) => (
    <div data-testid="oidc-callback-handler" data-return-to={returnTo ?? ""} />
  ),
}));

import { CallbackPage } from "../CallbackPage";

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

  it("resolves the deep link from auth.user.state and passes it to the handler", () => {
    // Proves the wiring: CallbackPage reads auth.user?.state (not auth.state)
    // and runs it through resolveReturnTo before handing off.
    useAuthMock.mockReturnValue({
      isLoading: false,
      isAuthenticated: true,
      error: undefined,
      user: { state: { returnTo: "/catalog/services?displayNameContains=foo" } },
    });
    renderPage();
    expect(screen.getByTestId("oidc-callback-handler")).toHaveAttribute(
      "data-return-to",
      "/catalog/services?displayNameContains=foo",
    );
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
