import { useEffect, useState } from "react";

const DISMISS_KEY = "catalog-hierarchy-help-dismissed";

/**
 * Loads the persisted dismissed flag, defaulting to shown (not dismissed) on first visit (no
 * stored value yet) or when storage throws (private-window / thumbnail contexts). Mirrors the
 * loadExpanded/saveExpanded pattern in CatalogHierarchyPage.
 */
function loadDismissed(): boolean {
  try {
    return sessionStorage.getItem(DISMISS_KEY) === "true";
  } catch {
    return false;
  }
}

function saveDismissed(dismissed: boolean) {
  try {
    sessionStorage.setItem(DISMISS_KEY, dismissed ? "true" : "false");
  } catch {
    // best-effort per-viewer convenience — ignore storage failures
  }
}

/**
 * Persistent-but-dismissible explainer for the catalog hierarchy view. Plain markup only — no
 * react-aria overlay/tooltip/popover — to stay jsdom-safe and match this page's hand-built style.
 */
export function HierarchyHelpCallout() {
  const [dismissed, setDismissed] = useState<boolean>(loadDismissed);

  // Persisting is a side effect, so it lives in an effect keyed off the state it mirrors — not
  // inside the setDismissed updater — mirrors CatalogHierarchyPage's expand-state persistence.
  useEffect(() => {
    saveDismissed(dismissed);
  }, [dismissed]);

  return (
    <div className="mb-4 rounded-md border border-secondary bg-secondary px-4 py-3 text-sm">
      <div className="flex items-center justify-between gap-3">
        <h2 className="font-medium text-primary">About this view</h2>
        <button
          type="button"
          aria-expanded={!dismissed}
          onClick={() => setDismissed((prev) => !prev)}
          className="rounded-md border border-secondary px-2 py-1 text-xs text-secondary hover:bg-primary_hover"
        >
          {dismissed ? "Show about this view" : "Hide"}
        </button>
      </div>
      {!dismissed && (
        <div className="mt-2 text-tertiary">
          <p>
            Browse your catalog by structure — every team, the systems each team stewards, and the
            components inside them, top-down.
          </p>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li>
              Expand a team or system to drill in; the number on each node is how many components
              sit beneath it.
            </li>
            <li>
              {
                '"Ungrouped" under a team holds its components not yet assigned to a system — a cue to tidy ownership.'
              }
            </li>
            <li>
              {
                "A system's members appear under its steward team even when another team owns them, so per-team counts here won't match the Teams page."
              }
            </li>
            <li>Click a component to open its detail page.</li>
          </ul>
        </div>
      )}
    </div>
  );
}

export default HierarchyHelpCallout;
