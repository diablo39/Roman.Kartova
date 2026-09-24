import { z } from "zod";

export const environmentTypes = ["development", "staging", "production"] as const;

export const registerEnvironmentSchema = z.object({
  displayName: z.string().trim().min(1, "Name is required").max(128),
  description: z.string().trim().min(1, "Description is required").max(4096),
  type: z.enum(environmentTypes),
  region: z.string().trim().max(256).optional().or(z.literal("")),
});

export type RegisterEnvironmentForm = z.infer<typeof registerEnvironmentSchema>;

// A2: full-replacement edit shares the exact register field set — no field is immutable
// (Environment has no owning team to protect, unlike editVmSchema's teamId omission).
export const editEnvironmentSchema = registerEnvironmentSchema;
export type EditEnvironmentForm = z.infer<typeof editEnvironmentSchema>;
