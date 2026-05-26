import { z } from "zod";

export const createTeamSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display name is required")
    .max(128, "Max 128 characters"),
  description: z
    .string()
    .max(512, "Max 512 characters")
    .optional()
    .or(z.literal("")),
});

export type CreateTeamInput = z.infer<typeof createTeamSchema>;
