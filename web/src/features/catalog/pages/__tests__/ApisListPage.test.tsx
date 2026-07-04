import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: {
      access_token: "t",
      profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" },
    },
  }),
}));

const listResult = {
  items: [{ id: "a1", displayName: "Orders API", style: "rest", version: "v1", teamId: "team1", createdBy: null, createdAt: "2026-07-04T00:00:00Z" }],
  isLoading: false, isError: false, error: null, hasNext: false, hasPrev: false,
  goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(),
};
vi.mock("@/features/catalog/api/apis", () => ({
  useApisList: () => listResult,
  useRegisterApi: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isError: false }) }));
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: () => true, isLoading: false }),
}));

import { ApisListPage } from "../ApisListPage";

describe("ApisListPage", () => {
  it("renders the APIs heading, the register button, and a row", () => {
    render(<MemoryRouter><ApisListPage /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "APIs" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Register API/i })).toBeInTheDocument();
    expect(screen.getByText("Orders API")).toBeInTheDocument();
  });
});
