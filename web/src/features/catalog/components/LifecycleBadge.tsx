import { Badge } from "@/components/base/badges/badges";
import type { Lifecycle } from "@/features/catalog/api/applications";

export interface LifecycleBadgeProps {
  lifecycle: Lifecycle;
  sunsetDate?: string | null;
  size?: "sm" | "md";
  /** When true (detail page), render a "Sunset: <date>" subline below the pill
   *  for `Deprecated` lifecycles. List cells leave this off. */
  showSunsetSubline?: boolean;
}

// Wire casing is camelCase (OpenAPI Lifecycle enum: "active" | "deprecated" |
// "decommissioned"), so the records key on the same shape the SPA receives
// from the API.
const COLOR: Record<Lifecycle, "success" | "warning" | "gray"> = {
  active: "success",
  deprecated: "warning",
  decommissioned: "gray",
};

const LABEL: Record<Lifecycle, string> = {
  active: "Active",
  deprecated: "Deprecated",
  decommissioned: "Decommissioned",
};

export function LifecycleBadge({
  lifecycle,
  sunsetDate,
  size = "sm",
  showSunsetSubline = false,
}: LifecycleBadgeProps) {
  return (
    <span className="inline-flex flex-col items-start gap-0.5">
      <Badge color={COLOR[lifecycle]} type="pill-color" size={size}>
        {LABEL[lifecycle]}
      </Badge>
      {showSunsetSubline && lifecycle === "deprecated" && sunsetDate && (
        <span className="text-xs text-tertiary">
          Sunset: {new Date(sunsetDate).toLocaleDateString()}
        </span>
      )}
    </span>
  );
}
