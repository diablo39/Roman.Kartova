import { describe, expect, it } from "vitest";
import { buildHierarchyView } from "../buildHierarchyView";

const team = (id: string, displayName: string) => ({ id, displayName }) as never;

const baseResponse = {
  totalComponentCount: 1,
  truncated: false,
  teams: [
    {
      teamId: "A",
      componentCount: 1,
      systems: [
        { systemId: "S1", displayName: "Sys", componentCount: 1, members: [{ kind: "service", id: "m1", displayName: "Mem" }] },
      ],
      ungrouped: { componentCount: 0, members: [] },
    },
  ],
} as never;

describe("buildHierarchyView", () => {
  it("joins team names from the teams list", () => {
    const view = buildHierarchyView(baseResponse, [team("A", "Team Alpha")], "Acme");
    expect(view.orgName).toBe("Acme");
    expect(view.teams[0]?.name).toBe("Team Alpha");
    expect(view.teams[0]?.systems[0]?.members[0]?.name).toBe("Mem");
  });

  it("injects empty teams from the teams list with zero count", () => {
    const view = buildHierarchyView(baseResponse, [team("A", "Team Alpha"), team("B", "Team Beta")], "Acme");
    const beta = view.teams.find((t) => t.id === "B");
    expect(beta).toBeDefined();
    expect(beta?.count).toBe(0);
    expect(beta?.systems).toHaveLength(0);
    expect(beta?.ungrouped.members).toHaveLength(0);
  });

  it("sorts teams and systems by name ascending (case-insensitive)", () => {
    const resp = {
      totalComponentCount: 0,
      truncated: false,
      teams: [
        { teamId: "Z", componentCount: 0, systems: [
            { systemId: "s2", displayName: "beta", componentCount: 0, members: [] },
            { systemId: "s1", displayName: "Alpha", componentCount: 0, members: [] },
          ], ungrouped: { componentCount: 0, members: [] } },
      ],
    } as never;
    const view = buildHierarchyView(resp, [team("Z", "zebra"), team("A", "aardvark")], "Acme");
    expect(view.teams.map((t) => t.name)).toEqual(["aardvark", "zebra"]);
    const zebra = view.teams.find((t) => t.name === "zebra");
    expect(zebra?.systems.map((s) => s.name)).toEqual(["Alpha", "beta"]);
  });

  it("falls back to the team id when the name is unknown", () => {
    const view = buildHierarchyView(baseResponse, [], "Acme");
    expect(view.teams[0]?.name).toBe("A");
  });

  it("carries totalCount and truncated through", () => {
    const resp = { ...(baseResponse as object), truncated: true, totalComponentCount: 7 } as never;
    const view = buildHierarchyView(resp, [team("A", "Team Alpha")], "Acme");
    expect(view.truncated).toBe(true);
    expect(view.totalCount).toBe(7);
  });
});
