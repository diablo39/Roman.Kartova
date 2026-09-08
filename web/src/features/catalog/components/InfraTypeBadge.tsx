import { Badge } from "@/components/base/badges/badges";
import { infraTypeColor, infraTypeLabel, type InfraType } from "@/features/catalog/infraType";

export interface InfraTypeBadgeProps {
  type: InfraType;
  size?: "sm" | "md";
}

export function InfraTypeBadge({ type, size = "sm" }: InfraTypeBadgeProps) {
  return (
    <Badge color={infraTypeColor(type)} type="pill-color" size={size}>
      {infraTypeLabel(type)}
    </Badge>
  );
}
