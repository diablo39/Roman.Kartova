import { z } from "zod";
import { sunsetDateField } from "./sunsetDateField";

export const unDecommissionApplicationSchema = z.object({ sunsetDate: sunsetDateField });

export type UnDecommissionApplicationInput = z.infer<typeof unDecommissionApplicationSchema>;
