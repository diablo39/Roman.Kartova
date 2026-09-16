import { describe, it, expect } from "vitest";
import { isOwningTeamMemberOrAdmin } from "@/features/catalog/teamOwnership";

describe("isOwningTeamMemberOrAdmin", () => {
  it("returns true for OrgAdmin regardless of team membership", () => {
    expect(isOwningTeamMemberOrAdmin("OrgAdmin", [], "team-1")).toBe(true);
    expect(isOwningTeamMemberOrAdmin("OrgAdmin", ["team-2"], "team-1")).toBe(true);
  });

  it("returns true for a non-admin who is a member of the owning team", () => {
    expect(isOwningTeamMemberOrAdmin("Member", ["team-1", "team-2"], "team-1")).toBe(true);
  });

  it("returns false for a non-admin who is not a member of the owning team", () => {
    expect(isOwningTeamMemberOrAdmin("Member", ["team-2"], "team-1")).toBe(false);
  });

  it("returns false for a non-admin when teamId is null or undefined (no owning team yet)", () => {
    expect(isOwningTeamMemberOrAdmin("Member", ["team-1"], null)).toBe(false);
    expect(isOwningTeamMemberOrAdmin("Member", ["team-1"], undefined)).toBe(false);
  });
});
