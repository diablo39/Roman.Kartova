import { it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SystemMembershipRow } from "@/features/catalog/components/SystemMembershipRow";
import * as systems from "@/features/catalog/api/systems";
import * as perms from "@/shared/auth/usePermissions";

function mockMembership(
  systemId: string | null,
  systemDisplayName: string | null,
  overrides?: { isLoading?: boolean; isError?: boolean },
) {
  vi.spyOn(systems, "useComponentSystem").mockReturnValue({
    systemId,
    systemDisplayName,
    isLoading: overrides?.isLoading ?? false,
    isError: overrides?.isError ?? false,
  });
  vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
}
function mockPerms(can: boolean) {
  vi.spyOn(perms, "usePermissions").mockReturnValue({
    hasPermission: () => can, role: can ? "OrgAdmin" : "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
  } as never);
}
function renderRow() {
  return render(
    <MemoryRouter>
      <SystemMembershipRow componentKind="application" componentId="a1" componentDisplayName="Checkout" componentTeamId="t1" />
    </MemoryRouter>,
  );
}

beforeEach(() => vi.restoreAllMocks());

it("links the System and offers Change when assigned", () => {
  mockMembership("sys1", "Payments");
  mockPerms(true);
  renderRow();

  expect(screen.getByText("Payments").closest("a")).toHaveAttribute("href", "/catalog/systems/sys1");
  expect(screen.getByRole("button", { name: /change/i })).toBeInTheDocument();
});

it("offers Assign when unassigned", () => {
  mockMembership(null, null);
  mockPerms(true);
  renderRow();

  expect(screen.getByText(/not assigned/i)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /assign/i })).toBeInTheDocument();
});

it("hides the action for a user who cannot manage relationships", () => {
  mockMembership("sys1", "Payments");
  mockPerms(false);
  renderRow();

  expect(screen.getByText("Payments")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /change|assign/i })).toBeNull();
});

it("hides the action for a user who cannot manage relationships when unassigned (pairwise)", () => {
  mockMembership(null, null);
  mockPerms(false);
  renderRow();

  expect(screen.getByText(/not assigned/i)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /change|assign/i })).toBeNull();
});

it("shows a loading placeholder and no action while the membership is in flight — never 'Not assigned'", () => {
  mockMembership(null, null, { isLoading: true });
  mockPerms(true);
  renderRow();

  expect(screen.queryByText(/not assigned/i)).toBeNull();
  expect(screen.queryByRole("button", { name: /change|assign/i })).toBeNull();
});

it("shows an error state and no action when the membership fails to load — never 'Not assigned'", () => {
  mockMembership(null, null, { isError: true });
  mockPerms(true);
  renderRow();

  expect(screen.getByText(/couldn.t load system membership/i)).toBeInTheDocument();
  expect(screen.queryByText(/not assigned/i)).toBeNull();
  expect(screen.queryByRole("button", { name: /change|assign/i })).toBeNull();
});
