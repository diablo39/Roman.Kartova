import { z } from "zod";

export const environmentTypes = ["development", "staging", "production"] as const;

export const registerEnvironmentSchema = z.object({
  displayName: z.string().trim().min(1, "Name is required").max(128),
  description: z.string().trim().min(1, "Description is required").max(4096),
  type: z.enum(environmentTypes),
  region: z.string().trim().max(256).optional().or(z.literal("")),
});

export type RegisterEnvironmentForm = z.infer<typeof registerEnvironmentSchema>;

// A2: `type` is immutable on edit (design §"Domain": "Edit (metadata only — Type
// immutable)") — omitted here, mirroring editVmSchema's `teamId` omission.
export const editEnvironmentSchema = registerEnvironmentSchema.omit({ type: true });
export type EditEnvironmentForm = z.infer<typeof editEnvironmentSchema>;
