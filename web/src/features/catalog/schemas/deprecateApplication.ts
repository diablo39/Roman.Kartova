import { z } from "zod";
import { sunsetDateField } from "./sunsetDateField";

export const deprecateApplicationSchema = z.object({
  sunsetDate: sunsetDateField,
  // ADR-0110: optional successor set at deprecate time. Omitted entirely
  // when no successor is picked (server default = no successor).
  successorApplicationId: z.string().uuid().optional(),
});

export type DeprecateApplicationInput = z.infer<typeof deprecateApplicationSchema>;
