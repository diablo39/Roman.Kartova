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

it("clicking the in chevron toggles expand in", async () => {
  const a = renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableIn: true });
  await userEvent.click(screen.getByRole("button", { name: /expand dependents/i }));
  expect(a.toggleExpand).toHaveBeenCalledWith("service:s", "in");
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
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  await userEvent.click(screen.getByRole("menuitem", { name: /open page/i }));
  expect(a.openPage).toHaveBeenCalledWith("service", "s");
});

it("drops the expand items from the ⋯ menu when supportsExpand is false (mini-graph preview)", async () => {
  renderNode(
    { kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 3 },
    { supportsExpand: false },
  );
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  expect(screen.queryByRole("menuitem", { name: /expand|collapse/i })).toBeNull();
  expect(screen.getByRole("menuitem", { name: /set as focus/i })).toBeInTheDocument();
  expect(screen.getByRole("menuitem", { name: /open page/i })).toBeInTheDocument();
});

it("hides the expand chevrons when supportsExpand is false", () => {
  renderNode(
    { kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, expandableIn: true },
    { supportsExpand: false },
  );
  expect(screen.queryByRole("button", { name: /expand dependencies|expand dependents/i })).toBeNull();
});

it("renders the application kind label", () => {
  renderNode({ kind: "application", entityId: "a", displayName: "A", side: "dependency" });
  expect(screen.getByText("Application")).toBeInTheDocument();
});

it("renders the service kind label", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency" });
  expect(screen.getByText("Service")).toBeInTheDocument();
});

it("emphasizes a focused node with font-semibold", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "focused" });
  expect(screen.getByText("S").closest("div[class*='font-semibold']")).toBeInTheDocument();
});

it("applies selected styling with border-brand-solid", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", selected: true });
  expect(screen.getByText("S").closest("div[class*='border-brand-solid']")).toBeInTheDocument();
});

it("applies dimmed styling with opacity-30", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", dimmed: true });
  expect(screen.getByText("S").closest("div[class*='opacity-30']")).toBeInTheDocument();
});

it("clicking the expand chevron does not select the node (stopPropagation contract)", async () => {
  const selectSpy = vi.fn();
  const value: GraphActions = { toggleExpand: vi.fn(), setFocus: vi.fn(), openPage: vi.fn(), atCap: false };
  render(
    <div onClick={selectSpy}>
      <GraphActionsProvider value={value}>
        <EntityGraphNode
          {...({
            data: { kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true },
          } as unknown as Parameters<typeof EntityGraphNode>[0])}
        />
      </GraphActionsProvider>
    </div>,
  );
  await userEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
  expect(value.toggleExpand).toHaveBeenCalledWith("service:s", "out");
  expect(selectSpy).not.toHaveBeenCalled();
});

it("opening the ⋯ menu does not select the node (stopPropagation contract)", async () => {
  const selectSpy = vi.fn();
  const value: GraphActions = { toggleExpand: vi.fn(), setFocus: vi.fn(), openPage: vi.fn(), atCap: false };
  render(
    <div onClick={selectSpy}>
      <GraphActionsProvider value={value}>
        <EntityGraphNode
          {...({
            data: { kind: "service", entityId: "s", displayName: "S", side: "dependency" },
          } as unknown as Parameters<typeof EntityGraphNode>[0])}
        />
      </GraphActionsProvider>
    </div>,
  );
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  expect(selectSpy).not.toHaveBeenCalled();
});

it("shows the unloaded-out count as the menu item addon when not expanded", async () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 3 });
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  expect(screen.getByRole("menuitem", { name: /expand dependencies/i })).toHaveTextContent("3");
});

it("shows no count addon on the menu item when the direction is already expanded", async () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandedOut: true, unloadedOut: 3 });
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  const item = screen.getByRole("menuitem", { name: /collapse dependencies/i });
  expect(item).not.toHaveTextContent("3");
});

it("keeps the collapse chevron enabled at cap when the direction is already expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandedOut: true }, { atCap: true });
  expect(screen.getByRole("button", { name: /collapse dependencies/i })).not.toBeDisabled();
});

it("renders a tier-1 glow ring when impactTier is 1", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", impactTier: 1 });
  expect(
    screen.getByText("S").closest("div[class*='ring-[color:var(--color-bg-error-solid)]']"),
  ).toBeInTheDocument();
});

it("renders a tier-2 glow ring when impactTier is 2", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", impactTier: 2 });
  expect(
    screen.getByText("S").closest("div[class*='ring-[color:var(--color-bg-warning-solid)]']"),
  ).toBeInTheDocument();
});

it("renders a tier-3 glow ring distinct from tier-2 and the ≥4 brand fallback", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", impactTier: 3 });
  const el = screen.getByText("S").closest("div[class*='ring-2']");
  expect(el).toBeInTheDocument();
  expect(el).toHaveClass("ring-[color:var(--color-bg-success-solid)]");
  expect(el).not.toHaveClass("ring-[color:var(--color-bg-warning-solid)]");
  expect(el).not.toHaveClass("ring-[color:var(--color-bg-brand-solid)]");
});

it("renders no glow ring for the focus node (impactTier 0) or when impactTier is undefined", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "focused", impactTier: 0 });
  expect(screen.getByText("S").closest("div[class*='ring-2']")).toBeNull();

  renderNode({ kind: "service", entityId: "s2", displayName: "S2", side: "dependency" });
  expect(screen.getByText("S2").closest("div[class*='ring-2']")).toBeNull();
});
