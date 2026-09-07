import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";

type Props = {
  label: string;
  count: number;
  to?: string;
  expanded: boolean;
  hasChildren: boolean;
  onToggle: () => void;
  onSelect?: () => void;
  depth: number;
  children?: React.ReactNode;
  testId?: string;
};

const rowClass = "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-primary_hover";

function CountBadge({ count }: { count: number }) {
  return (
    <span className="ml-auto rounded-full bg-secondary px-2 py-0.5 text-xs text-tertiary" aria-hidden={false}>
      {count}
    </span>
  );
}

/**
 * One row in the catalog hierarchy tree. A branch (hasChildren) is a toggle button carrying
 * aria-expanded; a leaf with `to` is a NavLink to its detail page. Indentation follows `depth`.
 * Children mount only when `expanded` — the Definition-tab / disclosure pattern, cheap and jsdom-safe
 * (no react-aria collection component, so no isRowHeader-style blank-page footgun — ADR-0084).
 */
export function HierarchyTreeNode({
  label, count, to, expanded, hasChildren, onToggle, onSelect, depth, children, testId,
}: Props) {
  const indent = { paddingLeft: `${depth * 16 + 8}px` };

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
          <span className="truncate">{label}</span>
          <CountBadge count={count} />
        </button>
      ) : to ? (
        <NavLink to={to} className={cx(rowClass, "text-secondary")} style={indent}
          onClick={() => onSelect?.()} data-testid={testId}>
          <span className="truncate">{label}</span>
        </NavLink>
      ) : (
        <div className={cx(rowClass, "text-tertiary")} style={indent} data-testid={testId}>
          <span className="truncate">{label}</span>
          <CountBadge count={count} />
        </div>
      )}
      {hasChildren && expanded && <ul className="space-y-0.5">{children}</ul>}
    </li>
  );
}
