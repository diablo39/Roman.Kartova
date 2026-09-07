import { useEffect, useMemo, useState } from "react";
import { useCatalogHierarchy } from "../api/hierarchy";
import { useTeamsList } from "@/features/teams/api/teams";
import { useOrgProfile } from "@/features/organization/api/organization";
import { buildHierarchyView, type HierarchyView, type MemberView } from "../hierarchy/buildHierarchyView";
import { HierarchyTreeNode } from "../hierarchy/HierarchyTreeNode";

const EXPAND_KEY = "catalog-hierarchy-expanded";
const ORG_NODE_KEY = "org";

/**
 * Loads persisted expand/collapse state, defaulting the org root open on first visit (no stored
 * state yet) or when storage throws (private-window / thumbnail contexts). A key set is treated
 * literally once it exists — a user who collapses the root gets that choice back on reload.
 *
 * The stored value is untrusted (another tab/version could have written a different shape, or a
 * user could hand-edit it via devtools), so the parsed JSON is validated before use: anything that
 * isn't an array of strings falls back to the default rather than propagating a bad cast into
 * `Set<string>`.
 */
function loadExpanded(): Set<string> {
  try {
    const raw = sessionStorage.getItem(EXPAND_KEY);
    if (!raw) return new Set([ORG_NODE_KEY]);
    const parsed: unknown = JSON.parse(raw);
    const keys = Array.isArray(parsed)
      ? parsed.filter((x): x is string => typeof x === "string")
      : [ORG_NODE_KEY];
    return new Set(keys);
  } catch {
    return new Set([ORG_NODE_KEY]);
  }
}

function saveExpanded(set: Set<string>) {
  try {
    sessionStorage.setItem(EXPAND_KEY, JSON.stringify([...set]));
  } catch {
    // best-effort per-viewer convenience — ignore storage failures
  }
}

export default function CatalogHierarchyPage() {
  const hierarchy = useCatalogHierarchy();
  const teams = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const org = useOrgProfile();

  const [expanded, setExpanded] = useState<Set<string>>(loadExpanded);
  const [selectedPath, setSelectedPath] = useState<string[]>([]); // breadcrumb labels

  // Persisting is a side effect, so it lives in an effect keyed off the state it mirrors — not
  // inside the `setExpanded` updater, which React (StrictMode) may invoke more than once per
  // update and which must stay pure.
  useEffect(() => {
    saveExpanded(expanded);
  }, [expanded]);

  const view: HierarchyView | null = useMemo(() => {
    if (!hierarchy.data) return null;
    return buildHierarchyView(
      hierarchy.data,
      teams.items ?? [],
      org.data?.displayName ?? "Organization",
    );
  }, [hierarchy.data, teams.items, org.data?.displayName]);

  const toggle = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      next.has(key) ? next.delete(key) : next.add(key);
      return next;
    });

  // One member leaf, shared by the system-members list and the per-team ungrouped bucket — the
  // only difference between the two call sites is the breadcrumb ancestry to select. Always
  // passes `to` (never the hasChildren-less, to-less plain-div branch of HierarchyTreeNode).
  const renderMemberLeaf = (member: MemberView, ancestry: string[]) => (
    <HierarchyTreeNode
      key={`${member.kind}:${member.id}`}
      label={member.name}
      count={0}
      expanded={false}
      hasChildren={false}
      to={`/catalog/${member.kind === "application" ? "applications" : "services"}/${member.id}`}
      onToggle={() => {}}
      onSelect={() => setSelectedPath(ancestry)}
      depth={3}
    />
  );

  if (hierarchy.isLoading) return <div className="p-6 text-secondary">Loading hierarchy…</div>;
  if (hierarchy.isError || !view)
    return <div className="p-6 text-error-primary">Could not load the catalog hierarchy.</div>;

  return (
    <div className="p-6">
      <h1 className="mb-1 text-xl font-semibold text-primary">Catalog hierarchy</h1>
      <p className="mb-4 text-sm text-tertiary">
        Browse by organization, team, and system. {view.totalCount} component
        {view.totalCount === 1 ? "" : "s"}.
      </p>

      {/*
        Trail reflects the selected node's ancestry: empty until something is picked, so the org
        name isn't shown twice (once here, once as the root tree row's own label) on first paint.
      */}
      <nav aria-label="Breadcrumb" data-testid="hierarchy-breadcrumb" className="mb-3 text-sm text-tertiary">
        {selectedPath.length > 0 ? [view.orgName, ...selectedPath].join("  /  ") : null}
      </nav>

      {view.truncated && (
        <div className="mb-3 rounded-md bg-warning-secondary px-3 py-2 text-sm text-warning-primary">
          Showing the first {view.totalCount} components — some are not listed.
        </div>
      )}

      <ul className="space-y-0.5">
        <HierarchyTreeNode
          label={view.orgName}
          count={view.totalCount}
          expanded={expanded.has(ORG_NODE_KEY)}
          hasChildren
          onToggle={() => toggle(ORG_NODE_KEY)}
          onSelect={() => setSelectedPath([])}
          depth={0}
          testId="node-org"
        >
          {view.teams.map((team) => {
            const teamKey = `team:${team.id}`;
            return (
              <HierarchyTreeNode
                key={teamKey}
                label={team.name}
                count={team.count}
                expanded={expanded.has(teamKey)}
                hasChildren
                onToggle={() => toggle(teamKey)}
                onSelect={() => setSelectedPath([team.name])}
                depth={1}
                testId={`node-${teamKey}`}
              >
                {team.systems.map((sys) => {
                  const sysKey = `sys:${sys.id}`;
                  return (
                    <HierarchyTreeNode
                      key={sysKey}
                      label={sys.name}
                      count={sys.count}
                      expanded={expanded.has(sysKey)}
                      hasChildren
                      onToggle={() => toggle(sysKey)}
                      onSelect={() => setSelectedPath([team.name, sys.name])}
                      depth={2}
                      testId={`node-${sysKey}`}
                    >
                      {sys.members.map((m) => renderMemberLeaf(m, [team.name, sys.name, m.name]))}
                    </HierarchyTreeNode>
                  );
                })}
                <HierarchyTreeNode
                  label="Ungrouped"
                  count={team.ungrouped.count}
                  expanded={expanded.has(`ungrouped:${team.id}`)}
                  hasChildren
                  onToggle={() => toggle(`ungrouped:${team.id}`)}
                  onSelect={() => setSelectedPath([team.name, "Ungrouped"])}
                  depth={2}
                  testId={`node-ungrouped-${team.id}`}
                >
                  {team.ungrouped.members.map((m) => renderMemberLeaf(m, [team.name, "Ungrouped", m.name]))}
                </HierarchyTreeNode>
              </HierarchyTreeNode>
            );
          })}
        </HierarchyTreeNode>
      </ul>
    </div>
  );
}
