import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ImpactBanner } from "@/features/catalog/components/ImpactBanner";

describe("ImpactBanner", () => {
  it("summarizes total + per-tier counts and fires onClose", async () => {
    const onClose = vi.fn();
    render(<ImpactBanner total={12} tiers={[{ tier: 1, count: 3 }, { tier: 2, count: 5 }, { tier: 3, count: 4 }]} truncated={false} nodeCap={200} onClose={onClose} />);
    expect(screen.getByText(/12 downstream/)).toBeInTheDocument();
    expect(screen.getByText(/3× tier-1/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /close analysis/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("shows the cap note when truncated", () => {
    render(<ImpactBanner total={200} tiers={[{ tier: 1, count: 200 }]} truncated nodeCap={200} onClose={() => {}} />);
    expect(screen.getByText(/showing first 200/i)).toBeInTheDocument();
  });
});
