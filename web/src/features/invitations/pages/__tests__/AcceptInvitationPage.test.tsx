import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

// --- mocks declared before importing the module under test ---

const getInvitationAcceptContextMock = vi.fn();
const acceptInvitationMock = vi.fn();
vi.mock("@/features/invitations/api/acceptInvitation", () => ({
  getInvitationAcceptContext: (...args: unknown[]) =>
    getInvitationAcceptContextMock(...args),
  acceptInvitation: (...args: unknown[]) => acceptInvitationMock(...args),
}));

const signinRedirectMock = vi.fn().mockResolvedValue(undefined);
const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { AcceptInvitationPage } from "../AcceptInvitationPage";

const CONTEXT = {
  orgDisplayName: "Acme Corp",
  invitedByDisplayName: "Alice Smith",
  email: "bob@example.com",
  defaultDisplayName: "Bob Jones",
  role: "Member",
  expiresAt: "2026-07-01T00:00:00Z",
};

function renderPage(search = "?token=tok-abc") {
  return render(
    <MemoryRouter initialEntries={[`/accept-invitation${search}`]}>
      <AcceptInvitationPage />
    </MemoryRouter>,
  );
}

describe("AcceptInvitationPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthMock.mockReturnValue({ signinRedirect: signinRedirectMock });
  });

  // ── no token ────────────────────────────────────────────────────────────────

  it("shows 'invalid link' when token is absent and does NOT call getInvitationAcceptContext", () => {
    renderPage("");
    expect(screen.getByText(/invalid invitation link/i)).toBeInTheDocument();
    expect(getInvitationAcceptContextMock).not.toHaveBeenCalled();
  });

  // ── happy-path context load ──────────────────────────────────────────────────

  it("renders org name, invited-by text, email, role and form fields on success", async () => {
    getInvitationAcceptContextMock.mockResolvedValue(CONTEXT);
    renderPage();

    // Loading state first, then form
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: /join acme corp/i })).toBeInTheDocument(),
    );

    expect(screen.getByText(/alice smith invited you/i)).toBeInTheDocument();

    // Read-only email
    const emailInput = screen.getByDisplayValue("bob@example.com");
    expect(emailInput).toBeInTheDocument();
    expect(emailInput).toHaveAttribute("readonly");

    // Role indicator
    expect(screen.getByText(/member/i)).toBeInTheDocument();

    // Form fields
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /join/i })).toBeInTheDocument();
  });

  // ── context error: 410 ───────────────────────────────────────────────────────

  it("shows 'gone' message when getInvitationAcceptContext rejects with __status 410", async () => {
    const err = Object.assign(new Error("Gone"), { __status: 410 });
    getInvitationAcceptContextMock.mockRejectedValue(err);
    renderPage();

    await waitFor(() =>
      expect(
        screen.getByText(/invitation can no longer be used/i),
      ).toBeInTheDocument(),
    );
  });

  // ── context error: 404 / generic ─────────────────────────────────────────────

  it("shows 'invalid link' message when getInvitationAcceptContext rejects with __status 404", async () => {
    const err = Object.assign(new Error("Not Found"), { __status: 404 });
    getInvitationAcceptContextMock.mockRejectedValue(err);
    renderPage();

    await waitFor(() =>
      expect(
        screen.getByText(/invalid invitation link/i),
      ).toBeInTheDocument(),
    );
  });

  it("shows 'invalid link' message on generic context error", async () => {
    getInvitationAcceptContextMock.mockRejectedValue(new Error("Network error"));
    renderPage();

    await waitFor(() =>
      expect(
        screen.getByText(/invalid invitation link/i),
      ).toBeInTheDocument(),
    );
  });

  // ── successful submit ────────────────────────────────────────────────────────

  it("calls acceptInvitation with token+password+displayName (NOT confirmPassword) then signinRedirect", async () => {
    getInvitationAcceptContextMock.mockResolvedValue(CONTEXT);
    acceptInvitationMock.mockResolvedValue({ email: CONTEXT.email });
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /join/i })).toBeInTheDocument(),
    );

    await userEvent.clear(screen.getByLabelText(/display name/i));
    await userEvent.type(screen.getByLabelText(/display name/i), "Bobby");
    await userEvent.type(screen.getByLabelText(/^password/i), "SecureP@ss123!");
    await userEvent.type(
      screen.getByLabelText(/confirm password/i),
      "SecureP@ss123!",
    );
    await userEvent.click(screen.getByRole("button", { name: /join/i }));

    await waitFor(() => expect(acceptInvitationMock).toHaveBeenCalledTimes(1));
    expect(acceptInvitationMock).toHaveBeenCalledWith({
      token: "tok-abc",
      password: "SecureP@ss123!",
      displayName: "Bobby",
    });
    // confirmPassword must NOT be present
    expect(acceptInvitationMock).not.toHaveBeenCalledWith(
      expect.objectContaining({ confirmPassword: expect.anything() }),
    );

    await waitFor(() =>
      expect(signinRedirectMock).toHaveBeenCalledWith({
        login_hint: CONTEXT.email,
      }),
    );
  });

  // ── submit → 400 ────────────────────────────────────────────────────────────

  it("shows password field error when acceptInvitation rejects with __status 400", async () => {
    getInvitationAcceptContextMock.mockResolvedValue(CONTEXT);
    const err = Object.assign(new Error("Bad Request"), { __status: 400 });
    acceptInvitationMock.mockRejectedValue(err);
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /join/i })).toBeInTheDocument(),
    );

    await userEvent.type(screen.getByLabelText(/^password/i), "SecureP@ss123!");
    await userEvent.type(
      screen.getByLabelText(/confirm password/i),
      "SecureP@ss123!",
    );
    await userEvent.click(screen.getByRole("button", { name: /join/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/password does not meet requirements/i),
      ).toBeInTheDocument(),
    );
  });

  // ── submit → 410 ────────────────────────────────────────────────────────────

  it("switches to gone message when acceptInvitation rejects with __status 410", async () => {
    getInvitationAcceptContextMock.mockResolvedValue(CONTEXT);
    const err = Object.assign(new Error("Gone"), { __status: 410 });
    acceptInvitationMock.mockRejectedValue(err);
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /join/i })).toBeInTheDocument(),
    );

    await userEvent.type(screen.getByLabelText(/^password/i), "SecureP@ss123!");
    await userEvent.type(
      screen.getByLabelText(/confirm password/i),
      "SecureP@ss123!",
    );
    await userEvent.click(screen.getByRole("button", { name: /join/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/invitation can no longer be used/i),
      ).toBeInTheDocument(),
    );
  });

  // ── confirm-mismatch ─────────────────────────────────────────────────────────

  it("shows zod confirm-mismatch error and does NOT call acceptInvitation", async () => {
    getInvitationAcceptContextMock.mockResolvedValue(CONTEXT);
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /join/i })).toBeInTheDocument(),
    );

    await userEvent.type(screen.getByLabelText(/^password/i), "SecureP@ss123!");
    await userEvent.type(
      screen.getByLabelText(/confirm password/i),
      "DifferentPass!",
    );
    await userEvent.click(screen.getByRole("button", { name: /join/i }));

    await waitFor(() =>
      expect(screen.getByText(/passwords do not match/i)).toBeInTheDocument(),
    );
    expect(acceptInvitationMock).not.toHaveBeenCalled();
  });
});
