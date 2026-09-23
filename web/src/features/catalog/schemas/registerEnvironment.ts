import { z } from "zod";

export const environmentTypes = ["development", "staging", "production"] as const;

export const registerEnvironmentSchema = z.object({
  displayName: z.string().trim().min(1, "Name is required").max(128),
  description: z.string().trim().min(1, "Description is required").max(4096),
  type: z.enum(environmentTypes),
  region: z.string().trim().max(256).optional().or(z.literal("")),
});

export type RegisterEnvironmentForm = z.infer<typeof registerEnvironmentSchema>;
