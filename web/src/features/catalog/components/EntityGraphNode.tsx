import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { ChevronLeft, ChevronRight, Minus } from "@untitledui/icons";
import { Dropdown } from "@/components/base/dropdown/dropdown";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";
import { useGraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";

const stop = (e: { stopPropagation: () => void }) => e.stopPropagation();

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const { toggleExpand, setFocus, openPage, atCap } = useGraphActions();
  const key = `${data.kind}:${data.entityId}`;

  const base = "rounded-lg bg-primary px-3 py-2";
  const variant = data.selected
    ? "border-2 border-brand-solid shadow-md"
    : data.side === "focused"
      ? "border-2 border-secondary font-semibold shadow-sm"
      : "border border-secondary shadow-xs";
  const dim = data.dimmed ? "opacity-30" : "";

  const chevron = (dir: ExpandDir) => {
    const expandable = dir === "out" ? data.expandableOut : data.expandableIn;
    const expanded = dir === "out" ? data.expandedOut : data.expandedIn;
    if (!expandable && !expanded) return null;
    const noun = dir === "out" ? "dependencies" : "dependents";
    const label = `${expanded ? "Collapse" : "Expand"} ${noun}`;
    const disabled = atCap && !expanded;
    const Icon = expanded ? Minus : dir === "out" ? ChevronRight : ChevronLeft;
    const side = dir === "out" ? "-right-2.5" : "-left-2.5";
    return (
      <button
        type="button"
        aria-label={label}
        title={label}
        disabled={disabled}
        className={`nodrag nopan absolute top-1/2 ${side} flex size-5 -translate-y-1/2 items-center justify-center rounded-full bg-brand-solid text-white shadow-sm disabled:opacity-40`}
        onPointerDown={stop}
        onClick={(e) => { stop(e); toggleExpand(key, dir); }}
      >
        <Icon className="size-3" />
      </button>
    );
  };

  const expandItem = (dir: ExpandDir) => {
    const expandable = dir === "out" ? data.expandableOut : data.expandableIn;
    const expanded = dir === "out" ? data.expandedOut : data.expandedIn;
    const count = dir === "out" ? data.unloadedOut : data.unloadedIn;
    const noun = dir === "out" ? "dependencies" : "dependents";
    return (
      <Dropdown.Item
        label={`${expanded ? "Collapse" : "Expand"} ${noun}`}
        addon={!expanded && count ? String(count) : undefined}
        isDisabled={!expanded && (!expandable || atCap)}
        onAction={() => toggleExpand(key, dir)}
      />
    );
  };

  return (
    <div className={`${base} ${variant} ${dim} relative`}>
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      {chevron("in")}
      {chevron("out")}
      <div className="flex items-start gap-2">
        <div className="min-w-0">
          <div className="text-sm text-primary">{data.displayName}</div>
          <div className="text-xs text-tertiary">{ENTITY_KIND_LABEL[data.kind] ?? data.kind}</div>
        </div>
        <div className="nodrag nopan ml-auto" onPointerDown={stop} onClick={stop}>
          <Dropdown.Root>
            <Dropdown.DotsButton className="size-5" />
            <Dropdown.Popover>
              <Dropdown.Menu>
                {expandItem("out")}
                {expandItem("in")}
                <Dropdown.Separator />
                <Dropdown.Item label="Set as focus" onAction={() => setFocus(data.kind, data.entityId)} />
                <Dropdown.Item label="Open page ↗" onAction={() => openPage(data.kind, data.entityId)} />
              </Dropdown.Menu>
            </Dropdown.Popover>
          </Dropdown.Root>
        </div>
      </div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
