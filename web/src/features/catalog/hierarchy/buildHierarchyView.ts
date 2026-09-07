import type { components } from "@/generated/openapi";

type CatalogHierarchyResponse = components["schemas"]["CatalogHierarchyResponse"];
type TeamResponse = components["schemas"]["TeamResponse"];
type HierarchyMemberDto = components["schemas"]["HierarchyMemberDto"];

export type MemberView = { kind: "application" | "service"; id: string; name: string };
export type SystemView = { id: string; name: string; count: number; members: MemberView[] };
export type BucketView = { count: number; members: MemberView[] };
export type TeamView = {
  id: string;
  name: string;
  count: number;
  systems: SystemView[];
  ungrouped: BucketView;
};
export type HierarchyView = {
  orgName: string;
  totalCount: number;
  truncated: boolean;
  teams: TeamView[];
};

const byName = (a: { name: string }, b: { name: string }) =>
  a.name.localeCompare(b.name, undefined, { sensitivity: "base" });

const mapMembers = (members: readonly HierarchyMemberDto[]): MemberView[] =>
  members
    .map((m) => ({ kind: m.kind as MemberView["kind"], id: m.id, name: m.displayName }))
    .sort(byName);

/**
 * Merge the backend hierarchy (team IDs only) with the teams list to resolve names, inject teams that
 * appear in the org's teams list but own no component in the response (count 0), and sort teams + systems
 * by display name ascending, case-insensitive. Members keep the backend's order but are defensively
 * re-sorted by name. Pure — no hooks, no I/O — so it is unit-tested without a DOM.
 *
 * Note: the generated client types `componentCount` / `totalComponentCount` as `number | string`
 * (codegen quirk on the int32 format) — coerced to `number` here so the view-model's `count` /
 * `totalCount` fields stay strictly numeric for consumers.
 */
export function buildHierarchyView(
  response: CatalogHierarchyResponse,
  teams: readonly TeamResponse[],
  orgName: string,
): HierarchyView {
  const nameById = new Map(teams.map((t) => [t.id, t.displayName]));
  const present = new Set(response.teams.map((t) => t.teamId));

  const fromResponse: TeamView[] = response.teams.map((t) => ({
    id: t.teamId,
    name: nameById.get(t.teamId) ?? t.teamId,
    count: Number(t.componentCount),
    systems: t.systems
      .map((s) => ({
        id: s.systemId,
        name: s.displayName,
        count: Number(s.componentCount),
        members: mapMembers(s.members),
      }))
      .sort(byName),
    ungrouped: { count: Number(t.ungrouped.componentCount), members: mapMembers(t.ungrouped.members) },
  }));

  const injected: TeamView[] = teams
    .filter((t) => !present.has(t.id))
    .map((t) => ({ id: t.id, name: t.displayName, count: 0, systems: [], ungrouped: { count: 0, members: [] } }));

  return {
    orgName,
    totalCount: Number(response.totalComponentCount),
    truncated: response.truncated,
    teams: [...fromResponse, ...injected].sort(byName),
  };
}
