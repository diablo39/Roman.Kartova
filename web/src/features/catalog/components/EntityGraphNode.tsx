import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

const KIND_LABEL: Record<string, string> = { application: "Application", service: "Service" };

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const focused = data.side === "focused";
  return (
    <div
      className={
        focused
          ? "rounded-lg border-2 border-secondary bg-primary px-3 py-2 font-semibold shadow-sm"
          : "rounded-lg border border-secondary bg-primary px-3 py-2 shadow-xs"
      }
    >
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="text-sm text-primary">{data.displayName}</div>
      <div className="text-xs text-tertiary">{KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
