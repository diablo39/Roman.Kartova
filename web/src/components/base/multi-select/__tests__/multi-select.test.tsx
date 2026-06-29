import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MultiSelect } from "@/components/base/multi-select/multi-select";

const OPTIONS = [
  { label: "Application", value: "application" },
  { label: "Service", value: "service" },
];

describe("MultiSelect controlled mode", () => {
  it("reflects selectedKeys in the summary", () => {
    render(<MultiSelect name="k" aria-label="Kind" options={OPTIONS} selectedKeys={["application"]} onChange={() => {}} />);
    expect(screen.getByText("Application")).toBeInTheDocument();
  });

  it("fires onChange with the selected values when an option is picked", async () => {
    const onChange = vi.fn();
    render(<MultiSelect name="k" aria-label="Kind" options={OPTIONS} selectedKeys={[]} onChange={onChange} />);
    await userEvent.click(screen.getByLabelText("Kind"));
    await userEvent.click(screen.getByText("Service"));
    expect(onChange).toHaveBeenCalledWith(["service"]);
  });
});
