import { z } from "zod";

export const editApplicationSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display name is required.")
    .max(128, "Display name must be 128 characters or fewer.")
    .refine((v) => v.trim().length > 0, {
      message: "Display name must not be only whitespace.",
    }),
  description: z
    .string()
    .min(1, "Description is required.")
    .refine((v) => v.trim().length > 0, {
      message: "Description must not be only whitespace.",
    }),
});

export type EditApplicationInput = z.infer<typeof editApplicationSchema>;
