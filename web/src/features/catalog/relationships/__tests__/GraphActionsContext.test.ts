import { describe, it, expect, vi } from "vitest";
import { createReadOnlyGraphActions } from "../GraphActionsContext";

describe("createReadOnlyGraphActions", () => {
  it("routes setFocus through graphFocusPath", () => {
    const navigate = vi.fn();
    const actions = createReadOnlyGraphActions(navigate);
    actions.setFocus("system", "s1");
    expect(navigate).toHaveBeenCalledWith("/graph?focus=system:s1");
  });

  it("routes openPage through entityDetailPath", () => {
    const navigate = vi.fn();
    const actions = createReadOnlyGraphActions(navigate);
    actions.openPage("system", "s1");
    expect(navigate).toHaveBeenCalledWith("/catalog/systems/s1");
  });

  it("reports supportsExpand === false — the mini-graph/system-diagram contract, not the full explorer's", () => {
    const actions = createReadOnlyGraphActions(vi.fn());
    expect(actions.supportsExpand).toBe(false);
  });

  it("no-ops toggleExpand and reports atCap false", () => {
    const actions = createReadOnlyGraphActions(vi.fn());
    expect(() => actions.toggleExpand("system:s1", "out")).not.toThrow();
    expect(actions.atCap).toBe(false);
  });
});
