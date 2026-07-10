import { it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@xyflow/react", () => ({
  Handle: () => null,
  Position: { Left: "left", Right: "right" },
}));

import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphActionsProvider, type GraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

function renderNode(data: GraphNodeData, actions?: Partial<GraphActions>) {
  const value: GraphActions = {
    toggleExpand: vi.fn(), setFocus: vi.fn(), openPage: vi.fn(), atCap: false, ...actions,
  };
  render(
    <GraphActionsProvider value={value}>
      <EntityGraphNode {...({ data } as unknown as Parameters<typeof EntityGraphNode>[0])} />
    </GraphActionsProvider>,
  );
  return value;
}

it("shows an expand-dependencies chevron only when out is expandable", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 3 });
  expect(screen.getByRole("button", { name: /expand dependencies/i })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /expand dependents/i })).toBeNull();
});

it("hides both chevrons when nothing is expandable or expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency" });
  expect(screen.queryByRole("button", { name: /dependencies|dependents/i })).toBeNull();
});

it("shows a collapse chevron when the direction is expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandedOut: true });
  expect(screen.getByRole("button", { name: /collapse dependencies/i })).toBeInTheDocument();
});

it("clicking the out chevron toggles expand out", async () => {
  const a = renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true });
  await userEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
  expect(a.toggleExpand).toHaveBeenCalledWith("service:s", "out");
});

it("disables expand chevron at cap when not expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true }, { atCap: true });
  expect(screen.getByRole("button", { name: /expand dependencies/i })).toBeDisabled();
});

it("menu opens and fires set focus / open page", async () => {
  const a = renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 2 });
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  await userEvent.click(screen.getByRole("menuitem", { name: /set as focus/i }));
  expect(a.setFocus).toHaveBeenCalledWith("service", "s");
});
