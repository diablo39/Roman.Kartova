import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

vi.mock("@/shared/oidc/RequireAuth", () => ({
  RequireAuth: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));
vi.mock("../layout/AdminLayout", async () => {
  const { Outlet } = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { AdminLayout: () => <div data-testid="admin-shell"><Outlet /></div> };
});
vi.mock("../pages/AdminLandingPage", () => ({ AdminLandingPage: () => <div>landing</div> }));
vi.mock("../pages/AdminCallbackPage", () => ({ AdminCallbackPage: () => <div>callback</div> }));

import { AdminRoutes } from "../router";

const at = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AdminRoutes />
    </MemoryRouter>,
  );

describe("AdminRoutes", () => {
  it("renders the landing inside the protected shell at /", () => {
    at("/");
    expect(screen.getByTestId("admin-shell")).toContainElement(screen.getByText("landing"));
  });

  it("serves the OIDC callback outside the shell", () => {
    at("/callback");
    expect(screen.getByText("callback")).toBeInTheDocument();
    expect(screen.queryByTestId("admin-shell")).toBeNull();
  });

  it("redirects an unknown deep link to the landing", () => {
    at("/organizations/xyz");
    expect(screen.getByText("landing")).toBeInTheDocument();
  });
});
