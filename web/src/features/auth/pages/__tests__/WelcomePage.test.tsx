import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

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

import { WelcomePage } from "../WelcomePage";
import type { components } from "@/generated/openapi";

type AcceptedInvitationInfo = components["schemas"]["AcceptedInvitationInfo"];

const INVITE: AcceptedInvitationInfo = {
  orgDisplayName: "Acme Corp",
  invitedBy: { id: "u-2", displayName: "Bob Builder", email: "bob@example.com" },
  invitedAt: "2026-01-01T00:00:00Z",
  acceptedAt: "2026-01-02T00:00:00Z",
};

function renderAt(state: AcceptedInvitationInfo | null) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: "/welcome", state }]}>
      <Routes>
        <Route path="/welcome" element={<WelcomePage />} />
        <Route
          path="/catalog"
          element={<div data-testid="catalog-fallback">catalog</div>}
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe("WelcomePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the org name and inviter display name from router state", () => {
    renderAt(INVITE);
    expect(
      screen.getByRole("heading", { name: /welcome to acme corp/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/bob builder invited you to join/i),
    ).toBeInTheDocument();
  });

  it("falls back to inviter email when displayName is empty", () => {
    renderAt({
      ...INVITE,
      invitedBy: { id: "u-2", displayName: "", email: "bob@example.com" },
    });
    expect(
      screen.getByText(/bob@example\.com invited you to join/i),
    ).toBeInTheDocument();
  });

  it("clicking Continue navigates to /catalog with replace", async () => {
    renderAt(INVITE);
    await userEvent.click(
      screen.getByRole("button", { name: /continue to kartova/i }),
    );
    expect(navigateMock).toHaveBeenCalledWith("/catalog", { replace: true });
  });

  it("redirects to /catalog when navigated to without router state", () => {
    renderAt(null);
    expect(screen.getByTestId("catalog-fallback")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /welcome to/i })).toBeNull();
  });
});
