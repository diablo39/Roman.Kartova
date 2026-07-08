import { it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ApiSurfaceSection } from "@/features/catalog/components/ApiSurfaceSection";
import * as api from "@/features/catalog/api/apiSurface";

function mockSurface(data: api.ApiSurfaceResponse) {
  vi.spyOn(api, "useApiSurface").mockReturnValue({
    data,
    isLoading: false,
    isError: false,
  } as never);
}

function renderSection(entityKind: "service" | "application", entityId: string) {
  return render(
    <MemoryRouter>
      <ApiSurfaceSection entityKind={entityKind} entityId={entityId} />
    </MemoryRouter>,
  );
}

beforeEach(() => vi.restoreAllMocks());

it("renders provided and consumed APIs, with derived-origin via-link", () => {
  mockSurface({
    provides: [
      {
        apiId: "a1",
        displayName: "Orders API",
        style: "rest",
        version: "v1",
        hasSpec: true,
        origin: "derived",
        viaApplicationId: "app1",
        viaApplicationDisplayName: "Billing",
      },
    ],
    consumes: [
      {
        apiId: "a2",
        displayName: "Events API",
        style: "asyncApi",
        version: "2.0",
        hasSpec: false,
        origin: "direct",
        viaApplicationId: null,
        viaApplicationDisplayName: null,
      },
    ],
  });

  renderSection("service", "svc1");

  expect(screen.getByText("Orders API")).toBeInTheDocument();
  expect(screen.getByText("Events API")).toBeInTheDocument();
  // "Derived · via " and the "Billing" link are separate text nodes; match
  // the innermost <span> whose aggregated textContent contains the phrase.
  expect(
    screen.getAllByText(
      (_, element) =>
        element?.tagName.toLowerCase() === "span" &&
        /via billing/i.test(element.textContent ?? ""),
    ).length,
  ).toBeGreaterThan(0);
  // react-aria Table requires an isRowHeader column, or it throws at
  // TableCollection.updateColumns and blank-pages on a heavier render.
  expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
});

it("shows empty copy when list is empty", () => {
  mockSurface({ provides: [], consumes: [] });
  renderSection("application", "app1");
  expect(screen.getByText(/no apis provided/i)).toBeInTheDocument();
  expect(screen.getByText(/no apis consumed/i)).toBeInTheDocument();
});
