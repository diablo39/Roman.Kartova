import type { FC } from "react";
import { Building02, Users01, Package, Folder, Monitor01, Server01 } from "@untitledui/icons";

export type HierarchyNodeType = "org" | "team" | "system" | "ungrouped" | "application" | "service";

/**
 * Per-row visual identity for the catalog hierarchy tree: a leading icon and a short kind label,
 * so Team/System/Ungrouped/Application/Service rows are visually distinct instead of blending
 * together at a glance (E-03.F-03.S-02). Application/Service labels intentionally match
 * `ENTITY_KIND_LABEL` in `@/features/catalog/relationships/graphModel` (kept as literals here
 * rather than importing, to avoid coupling the tree's presentation module to the relationships
 * graph module for two string constants).
 */
export const HIERARCHY_NODE_META: Record<HierarchyNodeType, { label: string; Icon: FC<{ className?: string }> }> = {
  org: { label: "Organization", Icon: Building02 },
  team: { label: "Team", Icon: Users01 },
  system: { label: "System", Icon: Package },
  ungrouped: { label: "Ungrouped", Icon: Folder },
  application: { label: "Application", Icon: Monitor01 },
  service: { label: "Service", Icon: Server01 },
};
