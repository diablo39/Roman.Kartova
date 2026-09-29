import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SidebarFrame } from "../SidebarFrame";
import { DisabledItem, NavItemLink } from "../sidebar-nav";

describe("SidebarFrame", () => {
  it("renders the Kartova logo and the given nav", () => {
    render(
      <MemoryRouter>
        <SidebarFrame>
          <ul>
            <li><NavItemLink to="/" label="Overview" end /></li>
            <li><DisabledItem label="Organizations" hint="Coming soon" /></li>
          </ul>
        </SidebarFrame>
      </MemoryRouter>,
    );

    expect(screen.getByText("Kartova")).toBeInTheDocument();
    expect(screen.getByRole("navigation")).toContainElement(screen.getByRole("link", { name: "Overview" }));
    const disabled = screen.getByText("Organizations").closest("[data-disabled='true']");
    expect(disabled).not.toBeNull();
    expect(disabled).toHaveAttribute("title", "Coming soon");
    expect(screen.getByText("Coming soon")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /organizations/i })).toBeNull();
  });
});
