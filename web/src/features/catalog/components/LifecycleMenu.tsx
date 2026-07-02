import { useMemo, useState } from "react";
import { ChevronDown } from "@untitledui/icons";
import { Button as AriaButton } from "react-aria-components";

import { Dropdown } from "@/components/base/dropdown/dropdown";
import { LifecycleBadge } from "./LifecycleBadge";
import { DeprecateConfirmDialog } from "./DeprecateConfirmDialog";
import { DecommissionConfirmDialog } from "./DecommissionConfirmDialog";
import { ReactivateConfirmDialog } from "./ReactivateConfirmDialog";
import { UnDecommissionConfirmDialog } from "./UnDecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

interface Props {
  application: ApplicationResponse;
  /** Gate forward-lifecycle items (Deprecate, Decommission). Defaults to true for back-compat. */
  canForward?: boolean;
  /** Gate reverse-lifecycle items (Reactivate, Restore to Deprecated). Defaults to true for back-compat. */
  canReverse?: boolean;
  /**
   * Whether the user holds the sunset-override permission. When true, the
   * Decommission item stays enabled before the sunset date so the override
   * checkbox in DecommissionConfirmDialog is reachable (ADR-0073 admin override).
   * Defaults to false — non-holders keep the "disabled until sunset" behaviour.
   */
  canOverride?: boolean;
}

type DialogKind = "deprecate" | "decommission" | "reactivate" | "unDecommission" | null;

interface MenuItem {
  key: "deprecate" | "decommission" | "reactivate" | "unDecommission";
  label: string;
  isDisabled: boolean;
  addon?: string;
}

/**
 * Build the state-driven menu-item list for a given application.
 *
 * Pure, plain-arg signature so the impure `Date.now()` read can live in the
 * hook layer (in `useMemo`) instead of leaking into the JSX render path
 * (which the `react-hooks/purity` rule forbids).
 *
 * Forward items are gated by `canForward`; reverse items are gated by `canReverse`.
 */
function buildItems(
  lifecycle: ApplicationResponse["lifecycle"],
  sunsetDate: string | null,
  now: number,
  canForward: boolean,
  canReverse: boolean,
  canOverride: boolean,
): MenuItem[] {
  const items: MenuItem[] = [];

  if (lifecycle === "active" && canForward) {
    items.push({ key: "deprecate", label: "Deprecate…", isDisabled: false });
  }
  if (lifecycle === "deprecated" && canForward) {
    const sunset = sunsetDate ? new Date(sunsetDate) : null;
    const beforeSunset = sunset !== null && now < sunset.getTime();
    // Override holders may decommission before sunset (via the dialog's override
    // checkbox), so the item stays enabled for them; non-holders see it disabled
    // with the "After <date>" hint until the sunset elapses.
    const blockedBySunset = beforeSunset && !canOverride;
    items.push({
      key: "decommission",
      label: "Decommission",
      isDisabled: blockedBySunset,
      addon: blockedBySunset && sunset ? `After ${sunset.toLocaleDateString()}` : undefined,
    });
  }
  // Reverse items — OrgAdmin only.
  if ((lifecycle === "deprecated" || lifecycle === "decommissioned") && canReverse) {
    items.push({ key: "reactivate", label: "Reactivate…", isDisabled: false });
  }
  if (lifecycle === "decommissioned" && canReverse) {
    items.push({ key: "unDecommission", label: "Restore to Deprecated…", isDisabled: false });
  }

  return items;
}

/**
 * State-aware dropdown anchored on the lifecycle Badge.
 *
 *  - `active`        → "Deprecate…" item (opens DeprecateConfirmDialog) when canForward.
 *  - `deprecated`    → "Decommission" item (disabled until sunset date passes) when canForward;
 *                      "Reactivate…" when canReverse.
 *  - `decommissioned`→ "Reactivate…" + "Restore to Deprecated…" when canReverse;
 *                      otherwise badge only (no interactive trigger).
 *
 * Lifecycle wire shape is lowercase ("active" | "deprecated" |
 * "decommissioned") via JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
 * all comparisons in this file use those literals directly.
 */
export function LifecycleMenu({
  application,
  canForward = true,
  canReverse = true,
  canOverride = false,
}: Props) {
  const [openDialog, setOpenDialog] = useState<DialogKind>(null);

  // Lazy `useState` keeps the impure `Date.now()` read off the render path
  // (react-hooks/purity). A page sitting open across a sunset-date boundary
  // is resolved on the next cache invalidation.
  const [now] = useState(() => Date.now());

  const items = useMemo(
    () => buildItems(application.lifecycle, application.sunsetDate, now, canForward, canReverse, canOverride),
    [application.lifecycle, application.sunsetDate, now, canForward, canReverse, canOverride]
  );

  // No items available (e.g. decommissioned with no reverse permission, or active
  // viewer with no forward permission) — render the badge alone, no interactive trigger.
  if (items.length === 0) {
    return (
      <LifecycleBadge
        lifecycle={application.lifecycle}
        sunsetDate={application.sunsetDate}
        showSunsetSubline
      />
    );
  }

  return (
    <>
      <Dropdown.Root>
        <AriaButton
          aria-label="Open lifecycle menu"
          className="inline-flex cursor-pointer items-center gap-1 rounded-md outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
        >
          <LifecycleBadge
            lifecycle={application.lifecycle}
            sunsetDate={application.sunsetDate}
            showSunsetSubline
          />
          <ChevronDown aria-hidden="true" className="size-4 text-fg-quaternary" />
        </AriaButton>
        <Dropdown.Popover className="w-56" placement="bottom start">
          <Dropdown.Menu>
            {items.map((item) => (
              <Dropdown.Item
                key={item.key}
                label={item.label}
                addon={item.addon}
                isDisabled={item.isDisabled}
                onAction={() => setOpenDialog(item.key)}
              />
            ))}
          </Dropdown.Menu>
        </Dropdown.Popover>
      </Dropdown.Root>

      {openDialog === "deprecate" && (
        <DeprecateConfirmDialog
          application={application}
          open
          onOpenChange={(o) => setOpenDialog(o ? "deprecate" : null)}
        />
      )}
      {openDialog === "decommission" && (
        <DecommissionConfirmDialog
          application={application}
          open
          onOpenChange={(o) => setOpenDialog(o ? "decommission" : null)}
        />
      )}
      {openDialog === "reactivate" && (
        <ReactivateConfirmDialog
          application={application}
          open
          onOpenChange={(o) => setOpenDialog(o ? "reactivate" : null)}
        />
      )}
      {openDialog === "unDecommission" && (
        <UnDecommissionConfirmDialog
          application={application}
          open
          onOpenChange={(o) => setOpenDialog(o ? "unDecommission" : null)}
        />
      )}
    </>
  );
}
