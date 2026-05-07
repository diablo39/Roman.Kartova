import { useMemo, useState } from "react";
import { ChevronDown } from "@untitledui/icons";
import { Button as AriaButton } from "react-aria-components";

import { Dropdown } from "@/components/base/dropdown/dropdown";
import { LifecycleBadge } from "./LifecycleBadge";
import { DeprecateConfirmDialog } from "./DeprecateConfirmDialog";
import { DecommissionConfirmDialog } from "./DecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

interface Props {
  application: ApplicationResponse;
}

type DialogKind = "deprecate" | "decommission" | null;

interface MenuItem {
  key: "deprecate" | "decommission";
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
 */
function buildItems(
  lifecycle: ApplicationResponse["lifecycle"],
  sunsetDate: string | null,
  now: number
): MenuItem[] {
  if (lifecycle === "active") {
    return [{ key: "deprecate", label: "Deprecate…", isDisabled: false }];
  }
  if (lifecycle === "deprecated") {
    const sunset = sunsetDate ? new Date(sunsetDate) : null;
    const beforeSunset = sunset !== null && now < sunset.getTime();
    return [{
      key: "decommission",
      label: "Decommission",
      isDisabled: beforeSunset,
      addon: beforeSunset && sunset
        ? `After ${sunset.toLocaleDateString()}`
        : undefined,
    }];
  }
  return [];
}

/**
 * State-aware dropdown anchored on the lifecycle Badge.
 *
 *  - `active`        → "Deprecate…" item (opens DeprecateConfirmDialog).
 *  - `deprecated`    → "Decommission" item; disabled until sunset date has
 *                      passed, with explanatory hint when disabled.
 *  - `decommissioned`→ no menu — terminal state, badge only.
 *
 * Lifecycle wire shape is lowercase ("active" | "deprecated" |
 * "decommissioned") via JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
 * all comparisons in this file use those literals directly.
 */
export function LifecycleMenu({ application }: Props) {
  const [openDialog, setOpenDialog] = useState<DialogKind>(null);

  // Lazy `useState` keeps the impure `Date.now()` read off the render path
  // (react-hooks/purity). A page sitting open across a sunset-date boundary
  // is resolved on the next cache invalidation.
  const [now] = useState(() => Date.now());

  const items = useMemo(
    () => buildItems(application.lifecycle, application.sunsetDate, now),
    [application.lifecycle, application.sunsetDate, now]
  );

  // Decommissioned is a terminal state — render the badge alone with the
  // sunset-date subline (the badge already reads sunsetDate). No interactive
  // trigger.
  if (application.lifecycle === "decommissioned") {
    return (
      <LifecycleBadge
        lifecycle={application.lifecycle}
        sunsetDate={application.sunsetDate}
        showSunsetSubline
      />
    );
  }

  if (items.length === 0) return null;

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
    </>
  );
}
