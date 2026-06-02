import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

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

const signinRedirectMock = vi.fn().mockResolvedValue(undefined);
const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { LoginErrorPage } from "../LoginErrorPage";

function renderPage() {
  return render(
    <MemoryRouter>
      <LoginErrorPage />
    </MemoryRouter>,
  );
}

describe("LoginErrorPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthMock.mockReturnValue({ signinRedirect: signinRedirectMock });
  });

  it("renders the failure heading and recovery copy", () => {
    renderPage();
    expect(
      screen.getByRole("heading", { name: /sign-in failed/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/we couldn’t complete the sign-in/i)).toBeInTheDocument();
  });

  it("Go home navigates to /", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: /go home/i }));
    expect(navigateMock).toHaveBeenCalledWith("/");
  });

  it("Try again invokes auth.signinRedirect()", async () => {
    renderPage();
    await userEvent.click(screen.getByRole("button", { name: /try again/i }));
    expect(signinRedirectMock).toHaveBeenCalledTimes(1);
  });
});
