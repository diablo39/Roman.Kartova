import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({ useAuth: () => useAuthMock() }));

import { AdminCallbackPage } from "../pages/AdminCallbackPage";

function renderAt() {
  return render(
    <MemoryRouter initialEntries={["/callback"]}>
      <Routes>
        <Route path="/callback" element={<AdminCallbackPage />} />
        <Route path="/" element={<div>home</div>} />
        <Route path="/deep" element={<div>deep</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => useAuthMock.mockReset());

describe("AdminCallbackPage", () => {
  it("navigates to the validated returnTo after sign-in", async () => {
    useAuthMock.mockReturnValue({ isLoading: false, isAuthenticated: true, user: { state: { returnTo: "/deep" } } });
    renderAt();
    expect(await screen.findByText("deep")).toBeInTheDocument();
  });

  it("falls back to / when returnTo is missing or unsafe", async () => {
    useAuthMock.mockReturnValue({ isLoading: false, isAuthenticated: true, user: { state: { returnTo: "//evil.example" } } });
    renderAt();
    expect(await screen.findByText("home")).toBeInTheDocument();
  });

  it("shows the sign-in-failed panel on an OIDC error and retries", async () => {
    const signinRedirect = vi.fn();
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
    useAuthMock.mockReturnValue({ isLoading: false, isAuthenticated: false, error: new Error("state mismatch"), signinRedirect });
    renderAt();

    expect(screen.getByRole("heading", { name: "Sign-in failed" })).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try again" }));
    expect(signinRedirect).toHaveBeenCalledTimes(1);
  });

  it("shows progress while the code exchange is in flight", () => {
    useAuthMock.mockReturnValue({ isLoading: true, isAuthenticated: false });
    renderAt();
    expect(screen.getByText("Completing sign-in…")).toBeInTheDocument();
  });
});
