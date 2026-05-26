import { z } from "zod";

export const registerApplicationSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display Name must not be empty")
    .max(128, "Display Name must be at most 128 chars"),
  description: z
    .string()
    .min(1, "Description is required")
    .max(4096, "Description must be at most 4096 chars"),
});

export type RegisterApplicationInput = z.infer<typeof registerApplicationSchema>;
