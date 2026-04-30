import { z } from "zod";

const kebabCase = /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/;

export const registerApplicationSchema = z.object({
  name: z
    .string()
    .min(1, "Name is required")
    .max(64, "Name must be at most 64 chars")
    .regex(kebabCase, "Lowercase kebab-case (e.g. payment-gateway)"),
  displayName: z
    .string()
    .min(1, "Display name is required")
    .max(128, "Display name must be at most 128 chars"),
  description: z
    .string()
    .max(512, "Description must be at most 512 chars")
    .optional()
    .or(z.literal("")),
});

export type RegisterApplicationInput = z.infer<typeof registerApplicationSchema>;
