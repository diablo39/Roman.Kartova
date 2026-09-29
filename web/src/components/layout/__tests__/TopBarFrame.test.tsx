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

  it("a blank identity shows dashes in the menu, but the avatar keeps its ? placeholder (never a dash)", async () => {
    const user = userEvent.setup();
    render(
      <TopBarFrame identity={<span>x</span>} user={{ displayName: " ", email: "" }} onSignOut={vi.fn()} />,
    );

    // Avatar initials are derived from the RAW displayName, not the dashed fallback — a blank name
    // must render "?" (initialsOf's own placeholder), never "—" (gate-7 F2).
    const avatarInitials = screen.getByTestId("user-menu").querySelector("span");
    expect(avatarInitials).toHaveTextContent("?");
    expect(avatarInitials).not.toHaveTextContent("—");

    await user.click(screen.getByTestId("user-menu"));
    await screen.findByRole("menu");
    expect(document.querySelector(".font-medium.text-primary")).toHaveTextContent("—");
    expect(document.querySelector(".text-xs.text-tertiary")).toHaveTextContent("—");
  });
});
