import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { Skeleton } from "../skeleton";

describe("Skeleton", () => {
  it("renders a pulsing block with Untitled tokens", () => {
    render(<Skeleton data-testid="sk" className="h-4 w-32" />);
    const sk = screen.getByTestId("sk");
    expect(sk.className).toContain("animate-pulse");
    expect(sk.className).toContain("bg-secondary");
    expect(sk.className).toContain("rounded-md");
  });
});
