import { describe, expect, it } from "vitest";
import { Building02, Users01, Package, Folder, Monitor01, Server01 } from "@untitledui/icons";
import { HIERARCHY_NODE_META, type HierarchyNodeType } from "../hierarchyNodeMeta";

const ALL_TYPES: HierarchyNodeType[] = ["org", "team", "system", "ungrouped", "application", "service"];

const EXPECTED: Record<HierarchyNodeType, { label: string; Icon: unknown }> = {
  org: { label: "Organization", Icon: Building02 },
  team: { label: "Team", Icon: Users01 },
  system: { label: "System", Icon: Package },
  ungrouped: { label: "Ungrouped", Icon: Folder },
  application: { label: "Application", Icon: Monitor01 },
  service: { label: "Service", Icon: Server01 },
};

describe("HIERARCHY_NODE_META", () => {
  it("has an entry for every hierarchy node type with a non-empty label and a defined icon", () => {
    for (const type of ALL_TYPES) {
      const meta = HIERARCHY_NODE_META[type];
      expect(meta).toBeDefined();
      expect(meta.label.length).toBeGreaterThan(0);
      expect(typeof meta.Icon).toBe("function");
    }
  });

  it("maps each node type to its exact label and icon component", () => {
    for (const type of ALL_TYPES) {
      const meta = HIERARCHY_NODE_META[type];
      expect(meta.label).toBe(EXPECTED[type].label);
      expect(meta.Icon).toBe(EXPECTED[type].Icon);
    }
  });
});
