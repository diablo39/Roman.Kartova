import { useMemo, useState } from "react";
import { useCatalogHierarchy } from "../api/hierarchy";
import { useTeamsList } from "@/features/teams/api/teams";
import { useOrgProfile } from "@/features/organization/api/organization";
import { buildHierarchyView, type HierarchyView } from "../hierarchy/buildHierarchyView";
import { HierarchyTreeNode } from "../hierarchy/HierarchyTreeNode";

const EXPAND_KEY = "catalog-hierarchy-expanded";
const ORG_NODE_KEY = "org";

/**
 * Loads persisted expand/collapse state, defaulting the org root open on first visit (no stored
 * state yet) or when storage throws (private-window / thumbnail contexts). A key set is treated
 * literally once it exists — a user who collapses the root gets that choice back on reload.
 */
function loadExpanded(): Set<string> {
  try {
    const raw = sessionStorage.getItem(EXPAND_KEY);
    return new Set(raw ? (JSON.parse(raw) as string[]) : [ORG_NODE_KEY]);
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
      saveExpanded(next);
      return next;
    });

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
                      {sys.members.map((m) => (
                        <HierarchyTreeNode
                          key={`${m.kind}:${m.id}`}
                          label={m.name}
                          count={0}
                          expanded={false}
                          hasChildren={false}
                          to={`/catalog/${m.kind === "application" ? "applications" : "services"}/${m.id}`}
                          onToggle={() => {}}
                          onSelect={() => setSelectedPath([team.name, sys.name, m.name])}
                          depth={3}
                        />
                      ))}
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
                  {team.ungrouped.members.map((m) => (
                    <HierarchyTreeNode
                      key={`${m.kind}:${m.id}`}
                      label={m.name}
                      count={0}
                      expanded={false}
                      hasChildren={false}
                      to={`/catalog/${m.kind === "application" ? "applications" : "services"}/${m.id}`}
                      onToggle={() => {}}
                      onSelect={() => setSelectedPath([team.name, "Ungrouped", m.name])}
                      depth={3}
                    />
                  ))}
                </HierarchyTreeNode>
              </HierarchyTreeNode>
            );
          })}
        </HierarchyTreeNode>
      </ul>
    </div>
  );
}
