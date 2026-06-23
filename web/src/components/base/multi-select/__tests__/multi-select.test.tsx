// web/src/components/base/multi-select/__tests__/multi-select.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MultiSelect } from "../multi-select";

const OPTIONS = [
  { label: "Active", value: "active" },
  { label: "Deprecated", value: "deprecated" },
  { label: "Decommissioned", value: "decommissioned" },
];

function formOf(container: HTMLElement) {
  return container.querySelector("form") as HTMLFormElement;
}
// The trigger is the only button rendered by the control (the listbox is portaled).
const trigger = () => screen.getByRole("button");

describe("MultiSelect (base)", () => {
  it("shows the placeholder and emits no FormData value when nothing is selected", () => {
    const { container } = render(
      <form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} placeholder="Any status" /></form>,
    );
    expect(trigger()).toHaveTextContent("Any status");
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual([]);
  });

  it("seeds the selection + hidden inputs from defaultSelectedKeys", () => {
    const { container } = render(
      <form>
        <MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} defaultSelectedKeys={["active", "deprecated"]} />
      </form>,
    );
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual(["active", "deprecated"]);
    expect(trigger()).toHaveTextContent("2 selected");
  });

  it("shows the single option's label when exactly one is selected", () => {
    render(<form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} defaultSelectedKeys={["deprecated"]} /></form>);
    expect(trigger()).toHaveTextContent("Deprecated");
  });

  it("selecting options updates the FormData values", async () => {
    const { container } = render(
      <form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} placeholder="Any status" /></form>,
    );
    await userEvent.click(trigger());
    await userEvent.click(await screen.findByRole("option", { name: "Active" }));
    await userEvent.click(await screen.findByRole("option", { name: "Decommissioned" }));
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual(["active", "decommissioned"]);
  });
});
