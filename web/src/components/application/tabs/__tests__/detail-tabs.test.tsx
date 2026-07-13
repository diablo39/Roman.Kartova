import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation } from "react-router-dom";
import { DetailTabs } from "../detail-tabs";

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="loc">{loc.search}</div>;
}

function renderTabs(initial = "/x") {
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <DetailTabs aria-label="Entity">
        <DetailTabs.Tab id="overview" label="Overview"><p>overview-body</p></DetailTabs.Tab>
        <DetailTabs.Tab id="dependencies" label="Dependencies"><p>deps-body</p></DetailTabs.Tab>
        <DetailTabs.Tab id="definition" label="Definition"><p>def-body</p></DetailTabs.Tab>
      </DetailTabs>
      <LocationProbe />
    </MemoryRouter>,
  );
}

describe("DetailTabs", () => {
  it("renders all tab labels and shows the first panel by default", () => {
    renderTabs();
    expect(screen.getByRole("tab", { name: "Overview" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dependencies" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Definition" })).toBeInTheDocument();
    expect(screen.getByText("overview-body")).toBeInTheDocument();
    expect(screen.queryByText("deps-body")).not.toBeInTheDocument();
  });

  it("selects a tab on click and writes ?tab=", async () => {
    const user = userEvent.setup();
    renderTabs();
    await user.click(screen.getByRole("tab", { name: "Dependencies" }));
    expect(screen.getByText("deps-body")).toBeInTheDocument();
    expect(screen.getByTestId("loc").textContent).toContain("tab=dependencies");
  });

  it("honors an initial ?tab= deep-link", () => {
    renderTabs("/x?tab=definition");
    expect(screen.getByText("def-body")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Definition" })).toHaveAttribute("aria-selected", "true");
  });

  it("falls back to the first tab and normalizes an invalid ?tab=", () => {
    renderTabs("/x?tab=bogus");
    expect(screen.getByText("overview-body")).toBeInTheDocument();
    expect(screen.getByTestId("loc").textContent).toContain("tab=overview");
  });

  it("leaves the URL clean when no ?tab= is present", () => {
    renderTabs("/x");
    expect(screen.getByTestId("loc").textContent).toBe("");
  });
});
