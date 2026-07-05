import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { GraphFilterControls } from "@/features/catalog/components/GraphFilterControls";

const teams = [{ id: "t1", displayName: "Team One" }, { id: "t2", displayName: "Team Two" }];

describe("GraphFilterControls", () => {
  it("fires onKindsChange when a kind is picked", async () => {
    const onKindsChange = vi.fn();
    render(
      <GraphFilterControls
        kinds={[]} teamIds={[]} teams={teams} activeCount={0}
        onKindsChange={onKindsChange} onTeamIdsChange={() => {}} onClear={() => {}}
      />,
    );
    await userEvent.click(screen.getByLabelText("Filter by kind"));
    await userEvent.click(screen.getByText("Service"));
    expect(onKindsChange).toHaveBeenCalledWith(["service"]);
  });

  it("shows the active count and Clear, and Clear fires onClear", async () => {
    const onClear = vi.fn();
    render(
      <GraphFilterControls
        kinds={["application"]} teamIds={["t1"]} teams={teams} activeCount={2}
        onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={onClear}
      />,
    );
    expect(screen.getByText("Filters (2)")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(onClear).toHaveBeenCalled();
  });

  it("offers an API kind option", async () => {
    render(
      <GraphFilterControls
        kinds={[]} teamIds={[]} teams={teams} activeCount={0}
        onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={() => {}}
      />,
    );
    await userEvent.click(screen.getByLabelText("Filter by kind"));
    expect(screen.getByText("API")).toBeInTheDocument();
  });

  it("renders without Clear when no filter is active", () => {
    render(
      <GraphFilterControls
        kinds={[]} teamIds={[]} teams={[]} activeCount={0}
        onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={() => {}}
      />,
    );
    expect(screen.getByText("Filters")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
  });
});
