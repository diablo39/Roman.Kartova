import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { SystemBoundaryNode } from "../SystemBoundaryNode";

describe("SystemBoundaryNode", () => {
  it("renders the system name and sizes itself from its data", () => {
    const { container } = render(
      <SystemBoundaryNode
        {...({ data: { label: "Payments Platform", width: 300, height: 200 } } as unknown as Parameters<
          typeof SystemBoundaryNode
        >[0])}
      />,
    );

    expect(screen.getByText("Payments Platform")).toBeInTheDocument();
    const box = container.firstElementChild as HTMLElement;
    expect(box.style.width).toBe("300px");
    expect(box.style.height).toBe("200px");
    expect(box.style.pointerEvents).toBe("none");
  });
});
