import { describe, expect, it } from "vitest";
import { HIERARCHY_NODE_META, type HierarchyNodeType } from "../hierarchyNodeMeta";

const ALL_TYPES: HierarchyNodeType[] = ["org", "team", "system", "ungrouped", "application", "service"];

describe("HIERARCHY_NODE_META", () => {
  it("has an entry for every hierarchy node type with a non-empty label and a defined icon", () => {
    for (const type of ALL_TYPES) {
      const meta = HIERARCHY_NODE_META[type];
      expect(meta).toBeDefined();
      expect(meta.label.length).toBeGreaterThan(0);
      expect(typeof meta.Icon).toBe("function");
    }
  });
});
