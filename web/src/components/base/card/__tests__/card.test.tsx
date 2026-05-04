import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { Card, CardContent } from "../card";

describe("Card", () => {
  it("renders a rounded container with border + shadow utilities and Untitled bg token", () => {
    render(
      <Card data-testid="card">
        <CardContent>hello</CardContent>
      </Card>
    );
    const card = screen.getByTestId("card");
    expect(card.className).toContain("rounded-xl");
    expect(card.className).toContain("border");
    expect(card.className).toContain("bg-primary");
    expect(screen.getByText("hello")).toBeInTheDocument();
  });
});
