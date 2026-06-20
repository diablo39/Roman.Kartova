import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { HealthBadge } from "../HealthBadge";
import { healthLabel } from "@/features/catalog/health";

describe("HealthBadge", () => {
  it("renders the Unknown label for the default health", () => {
    render(<HealthBadge health="unknown" />);
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });

  it("renders the Healthy label", () => {
    render(<HealthBadge health="healthy" />);
    expect(screen.getByText("Healthy")).toBeInTheDocument();
  });
});

describe("healthLabel", () => {
  it("maps each enum value to a human label", () => {
    expect(healthLabel("unknown")).toBe("Unknown");
    expect(healthLabel("degraded")).toBe("Degraded");
    expect(healthLabel("unhealthy")).toBe("Unhealthy");
  });
});
