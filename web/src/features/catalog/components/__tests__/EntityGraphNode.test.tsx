import { it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@xyflow/react", () => ({
  Handle: () => null,
  Position: { Left: "left", Right: "right" },
}));

import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

function renderNode(data: GraphNodeData) {
  return render(<EntityGraphNode {...({ data } as unknown as Parameters<typeof EntityGraphNode>[0])} />);
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
