import { z } from "zod";
import { sunsetDateField } from "./sunsetDateField";

export const deprecateApplicationSchema = z.object({ sunsetDate: sunsetDateField });

export type DeprecateApplicationInput = z.infer<typeof deprecateApplicationSchema>;
