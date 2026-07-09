import { it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { DerivedDependenciesSection } from "@/features/catalog/components/DerivedDependenciesSection";
import * as api from "@/features/catalog/api/derivedDependencies";

function mock(data: api.DerivedDependenciesResponse) {
  vi.spyOn(api, "useDerivedDependencies").mockReturnValue({
    data,
    isLoading: false,
    isError: false,
  } as never);
}

function renderSection(entityId: string) {
  return render(
    <MemoryRouter>
      <DerivedDependenciesSection entityId={entityId} />
    </MemoryRouter>,
  );
}

beforeEach(() => vi.restoreAllMocks());

it("renders dependencies and dependents with provenance", () => {
  mock({
    dependencies: [
      {
        serviceId: "t1",
        displayName: "AuthService",
        teamId: null,
        paths: [
          { apiId: "a1", apiName: "Orders API", viaApplicationId: "app1", viaApplicationDisplayName: "Billing" },
        ],
      },
    ],
    dependents: [
      {
        serviceId: "s2",
        displayName: "Checkout",
        teamId: null,
        paths: [{ apiId: "a2", apiName: "Events API", viaApplicationId: null, viaApplicationDisplayName: null }],
      },
    ],
  });

  renderSection("svc1");

  expect(screen.getByText("AuthService")).toBeInTheDocument();
  expect(screen.getByText("Checkout")).toBeInTheDocument();
  expect(
    screen.getAllByText((_, el) => /via orders api/i.test(el?.textContent ?? "")).length,
  ).toBeGreaterThan(0);
  // ADR-0084: a populated react-aria Table must expose an isRowHeader column.
  expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
});

it("shows empty copy for both tables when there are no derived edges", () => {
  mock({ dependencies: [], dependents: [] });
  renderSection("svc1");
  expect(screen.getByText(/no derived dependencies/i)).toBeInTheDocument();
  expect(screen.getByText(/nothing derives a dependency on this service/i)).toBeInTheDocument();
});

it("shows an error message when the query fails", () => {
  vi.spyOn(api, "useDerivedDependencies").mockReturnValue({
    data: undefined,
    isLoading: false,
    isError: true,
  } as never);
  renderSection("svc1");
  expect(screen.getByText(/couldn.t load derived dependencies/i)).toBeInTheDocument();
});
