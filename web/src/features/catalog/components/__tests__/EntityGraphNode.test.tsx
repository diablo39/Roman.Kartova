import { it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";


vi.mock("@xyflow/react", () => ({
  Handle: () => null,
  Position: { Left: "left", Right: "right" },
}));

import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

function renderNode(data: GraphNodeData) {
  return render(
    <EntityGraphNode {...({ data } as unknown as Parameters<typeof EntityGraphNode>[0])} />,
  );
}

it("renders the displayName and a human kind label", () => {
  renderNode({ kind: "service", entityId: "s2", displayName: "AuthService", side: "dependency" });
  expect(screen.getByText("AuthService")).toBeInTheDocument();
  expect(screen.getByText("Service")).toBeInTheDocument();
});

it("renders the application kind label", () => {
  renderNode({ kind: "application", entityId: "a1", displayName: "Checkout", side: "dependent" });
  expect(screen.getByText("Application")).toBeInTheDocument();
});

it("emphasises the focused node and still renders its displayName", () => {
  renderNode({ kind: "service", entityId: "s1", displayName: "Me", side: "focused" });
  expect(screen.getByText("Me")).toBeInTheDocument();
  expect(screen.getByText("Me").closest("div[class*='font-semibold']")).toBeInTheDocument();
});

it("applies selected styling when data.selected is true", () => {
  renderNode({ kind: "service", entityId: "a", displayName: "A", side: "dependency", selected: true });
  expect(screen.getByText("A").closest("div[class*='border-brand-solid']")).not.toBeNull();
});

it("applies opacity-30 class when data.dimmed is true", () => {
  renderNode({ kind: "service", entityId: "d1", displayName: "Dimmed", side: "dependency", dimmed: true });
  expect(screen.getByText("Dimmed").closest("div[class*='opacity-30']")).not.toBeNull();
});

