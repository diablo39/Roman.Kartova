import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";
import { HIERARCHY_NODE_META, type HierarchyNodeType } from "./hierarchyNodeMeta";

type Props = {
  label: string;
  count: number;
  to?: string;
  expanded: boolean;
  hasChildren: boolean;
  onToggle: () => void;
  onSelect?: () => void;
  depth: number;
  nodeType: HierarchyNodeType;
  children?: React.ReactNode;
  testId?: string;
};

const rowClass = "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-primary_hover";

function CountBadge({ count }: { count: number }) {
  return (
    <span className="ml-auto rounded-full bg-secondary px-2 py-0.5 text-xs text-tertiary">
      {count}
    </span>
  );
}

/**
 * One row in the catalog hierarchy tree. A branch (hasChildren) is a toggle button carrying
 * aria-expanded; a leaf with `to` is a NavLink to its detail page. Indentation follows `depth`.
 * Children mount only when `expanded` — the Definition-tab / disclosure pattern, cheap and jsdom-safe
 * (no react-aria collection component, so no isRowHeader-style blank-page footgun — ADR-0084).
 *
 * Every row carries a leading type icon and a small kind label (from `HIERARCHY_NODE_META`,
 * keyed by the required `nodeType`) so Team/System/Ungrouped/Application/Service rows read as
 * visually distinct instead of blending together (E-03.F-03.S-02). The icon is decorative —
 * `aria-hidden` — because the kind label text already conveys the type to assistive tech.
 */
export function HierarchyTreeNode({
  label, count, to, expanded, hasChildren, onToggle, onSelect, depth, nodeType, children, testId,
}: Props) {
  const indent = { paddingLeft: `${depth * 16 + 8}px` };
  const meta = HIERARCHY_NODE_META[nodeType];
  const { Icon } = meta;

  return (
    <li>
      {hasChildren ? (
        <button
          type="button"
          className={cx(rowClass, "w-full text-left text-secondary")}
          style={indent}
          aria-expanded={expanded}
          onClick={() => { onToggle(); onSelect?.(); }}
          data-testid={testId}
        >
          <span aria-hidden className="text-tertiary">{expanded ? "▾" : "▸"}</span>
          <Icon className="size-4 shrink-0 text-tertiary" aria-hidden />
          <span className="truncate">{label}</span>
          <span className="text-xs text-tertiary">{meta.label}</span>
          <CountBadge count={count} />
        </button>
      ) : to ? (
        <NavLink to={to} className={cx(rowClass, "text-secondary")} style={indent}
          onClick={() => onSelect?.()} data-testid={testId}>
          <Icon className="size-4 shrink-0 text-tertiary" aria-hidden />
          <span className="truncate">{label}</span>
          <span className="text-xs text-tertiary">{meta.label}</span>
        </NavLink>
      ) : (
        <div className={cx(rowClass, "text-tertiary")} style={indent} data-testid={testId}>
          <Icon className="size-4 shrink-0 text-tertiary" aria-hidden />
          <span className="truncate">{label}</span>
          <span className="text-xs text-tertiary">{meta.label}</span>
          <CountBadge count={count} />
        </div>
      )}
      {hasChildren && expanded && <ul className="space-y-0.5">{children}</ul>}
    </li>
  );
}
