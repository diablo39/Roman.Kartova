import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TablePager } from "../data-table";

describe("<TablePager>", () => {
  it("disables Prev when hasPrev=false", () => {
    render(<TablePager hasPrev={false} hasNext={true} onPrev={() => {}} onNext={() => {}} pageSize={50} />);
    expect(screen.getByRole("button", { name: /prev/i })).toBeDisabled();
  });

  it("disables Next when hasNext=false", () => {
    render(<TablePager hasPrev={true} hasNext={false} onPrev={() => {}} onNext={() => {}} pageSize={50} />);
    expect(screen.getByRole("button", { name: /next/i })).toBeDisabled();
  });

  it("calls onNext / onPrev when buttons clicked", async () => {
    const onPrev = vi.fn();
    const onNext = vi.fn();
    const user = userEvent.setup();
    render(<TablePager hasPrev={true} hasNext={true} onPrev={onPrev} onNext={onNext} pageSize={50} />);
    await user.click(screen.getByRole("button", { name: /prev/i }));
    await user.click(screen.getByRole("button", { name: /next/i }));
    expect(onPrev).toHaveBeenCalledOnce();
    expect(onNext).toHaveBeenCalledOnce();
  });
});
