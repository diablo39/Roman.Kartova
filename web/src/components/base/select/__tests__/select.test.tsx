import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select } from "../select";

const OPTIONS = [
  { label: "All roles", value: "" },
  { label: "Viewer", value: "Viewer" },
  { label: "Member", value: "Member" },
];

function formOf(container: HTMLElement) {
  return container.querySelector("form") as HTMLFormElement;
}

describe("Select (base)", () => {
  // A react-aria Select trigger's accessible name is "<value> <label>" (e.g.
  // "Viewer Role"), not just the aria-label — so query the single trigger by
  // role and assert the displayed value via toHaveTextContent, never by an
  // exact { name: "Role" }.
  it("seeds the displayed value from defaultSelectedKey", () => {
    render(<Select aria-label="Role" options={OPTIONS} defaultSelectedKey="Viewer" />);
    expect(screen.getByRole("button")).toHaveTextContent("Viewer");
  });

  it("reports the empty default ('All roles') as an empty FormData value", () => {
    const { container } = render(
      <form><Select name="role" aria-label="Role" options={OPTIONS} defaultSelectedKey="" /></form>,
    );
    expect(new FormData(formOf(container)).get("role")).toBe("");
    expect(screen.getByRole("button")).toHaveTextContent("All roles");
  });

  it("selecting an option updates the FormData value", async () => {
    const { container } = render(
      <form><Select name="role" aria-label="Role" options={OPTIONS} defaultSelectedKey="" /></form>,
    );
    await userEvent.click(screen.getByRole("button"));
    await userEvent.click(await screen.findByRole("option", { name: "Member" }));
    expect(new FormData(formOf(container)).get("role")).toBe("Member");
  });
});
