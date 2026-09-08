import { Badge } from "@/components/base/badges/badges";
import { powerStateColor, powerStateLabel, type PowerState } from "@/features/catalog/powerState";

export interface PowerStateBadgeProps {
  powerState: PowerState;
  size?: "sm" | "md";
}

export function PowerStateBadge({ powerState, size = "sm" }: PowerStateBadgeProps) {
  return (
    <Badge color={powerStateColor(powerState)} type="pill-color" size={size}>
      {powerStateLabel(powerState)}
    </Badge>
  );
}
