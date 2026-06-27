import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const base = "rounded-lg bg-primary px-3 py-2";
  const variant = data.selected
    ? "border-2 border-brand-solid shadow-md"
    : data.side === "focused"
      ? "border-2 border-secondary font-semibold shadow-sm"
      : "border border-secondary shadow-xs";
  return (
    <div className={`${base} ${variant}`}>
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="text-sm text-primary">{data.displayName}</div>
      <div className="text-xs text-tertiary">{ENTITY_KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}