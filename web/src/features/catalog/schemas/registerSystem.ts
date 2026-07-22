import { z } from "zod";

export const registerSystemSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display Name must not be empty")
    .max(128, "Display Name must be at most 128 characters"),
  description: z.string().max(4096, "Description must be at most 4096 characters").optional(),
  teamId: z.string().uuid("Team is required"),
});

export type RegisterSystemInput = z.infer<typeof registerSystemSchema>;
