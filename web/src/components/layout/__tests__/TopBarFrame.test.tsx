import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TopBarFrame } from "../TopBarFrame";

describe("TopBarFrame", () => {
  it("renders the identity and center slots", () => {
    render(
      <TopBarFrame
        identity={<span>Platform Admin</span>}
        center={<input placeholder="Search entities..." />}
        user={null}
        onSignOut={vi.fn()}
      />,
    );
    expect(screen.getByText("Platform Admin")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Search entities...")).toBeInTheDocument();
  });

  it("renders no search box when center is omitted", () => {
    render(<TopBarFrame identity={<span>x</span>} user={null} onSignOut={vi.fn()} />);
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("shows the user and signs out from the user menu", async () => {
    const onSignOut = vi.fn();
    const user = userEvent.setup();
    render(
      <TopBarFrame
        identity={<span>x</span>}
        user={{ displayName: "Olga Operator", email: "olga@ops" }}
        onSignOut={onSignOut}
      />,
    );

    await user.click(screen.getByTestId("user-menu"));
    expect(await screen.findByText("Olga Operator")).toBeInTheDocument();
    expect(screen.getByText("olga@ops")).toBeInTheDocument();
    await user.click(screen.getByRole("menuitem", { name: /sign out/i }));

    expect(onSignOut).toHaveBeenCalledTimes(1);
  });
});
