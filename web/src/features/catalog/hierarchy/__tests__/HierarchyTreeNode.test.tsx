import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { HierarchyTreeNode } from "../HierarchyTreeNode";

describe("HierarchyTreeNode", () => {
  it("renders label and count, and toggles a branch via aria-expanded", async () => {
    const onToggle = vi.fn();
    render(
      <MemoryRouter>
        <HierarchyTreeNode label="Team Alpha" count={3} expanded={false} hasChildren onToggle={onToggle} depth={0} />
      </MemoryRouter>,
    );
    const btn = screen.getByRole("button", { name: /Team Alpha/ });
    expect(btn).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("3")).toBeInTheDocument();
    await userEvent.click(btn);
    expect(onToggle).toHaveBeenCalledOnce();
  });

  it("renders a leaf as a link to its detail page", () => {
    render(
      <MemoryRouter>
        <HierarchyTreeNode label="member-svc" count={0} expanded={false} hasChildren={false}
          to="/catalog/services/abc" onToggle={() => {}} depth={2} />
      </MemoryRouter>,
    );
    const link = screen.getByRole("link", { name: /member-svc/ });
    expect(link).toHaveAttribute("href", "/catalog/services/abc");
  });

  it("does not render children when collapsed", () => {
    render(
      <MemoryRouter>
        <HierarchyTreeNode label="Team" count={1} expanded={false} hasChildren onToggle={() => {}} depth={0}>
          <div>child-content</div>
        </HierarchyTreeNode>
      </MemoryRouter>,
    );
    expect(screen.queryByText("child-content")).not.toBeInTheDocument();
  });
});
