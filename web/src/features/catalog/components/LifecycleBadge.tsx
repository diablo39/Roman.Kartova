import { Badge } from "@/components/base/badges/badges";
import type { Lifecycle } from "@/features/catalog/api/applications";
import { lifecycleColor, lifecycleLabel } from "@/features/catalog/lifecycle";

export interface LifecycleBadgeProps {
  lifecycle: Lifecycle;
  sunsetDate?: string | null;
  size?: "sm" | "md";
  /** When true (detail page), render a "Sunset: <date>" subline below the pill
   *  for `Deprecated` lifecycles. List cells leave this off. */
  showSunsetSubline?: boolean;
}

export function LifecycleBadge({
  lifecycle,
  sunsetDate,
  size = "sm",
  showSunsetSubline = false,
}: LifecycleBadgeProps) {
  return (
    <span className="inline-flex flex-col items-start gap-0.5">
      <Badge color={lifecycleColor(lifecycle)} type="pill-color" size={size}>
        {lifecycleLabel(lifecycle)}
      </Badge>
      {showSunsetSubline && lifecycle === "deprecated" && sunsetDate && (
        <span className="text-xs text-tertiary">
          Sunset: {new Date(sunsetDate).toLocaleDateString()}
        </span>
      )}
    </span>
  );
}
