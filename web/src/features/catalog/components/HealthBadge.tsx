import { Badge } from "@/components/base/badges/badges";
import { healthColor, healthLabel, type Health } from "@/features/catalog/health";

export interface HealthBadgeProps {
  health: Health;
  size?: "sm" | "md";
}

export function HealthBadge({ health, size = "sm" }: HealthBadgeProps) {
  return (
    <Badge color={healthColor(health)} type="pill-color" size={size}>
      {healthLabel(health)}
    </Badge>
  );
}
