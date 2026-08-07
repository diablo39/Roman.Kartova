import type { Node, NodeProps } from "@xyflow/react";

export type SystemBoundaryData = { label: string; width: number; height: number };

/**
 * Non-interactive background band marking "inside this System" on the System diagram. Sits
 * behind the real nodes (zIndex set where the node object is built) and never takes pointer
 * events, so clicks fall through to the nodes on top of it.
 */
export function SystemBoundaryNode({ data }: NodeProps<Node<SystemBoundaryData>>) {
  return (
    <div
      // Solid border, deliberately not dashed (spec S6): dashed on a non-member node means "outside
      // this system", and the legend states that meaning once. A dashed band would contradict it —
      // the translucent brand fill already reads as a container without borrowing that channel.
      className="relative rounded-xl border border-brand bg-brand-primary/10"
      style={{ width: data.width, height: data.height, pointerEvents: "none" }}
    >
      <span className="absolute -top-5 left-1 text-xs font-semibold text-brand-secondary">
        {data.label}
      </span>
    </div>
  );
}
