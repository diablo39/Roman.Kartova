import { z } from "zod";

export const registerSystemSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display Name must not be empty")
    .max(128, "Display Name must be at most 128 characters"),
  description: z.string().max(4096, "Description must be at most 4096 characters").optional(),
  teamId: z.string().refine((val) => {
    // Valid UUID format: 8-4-4-4-12 hex digits
    const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    return uuidRegex.test(val);
  }, "Team is required"),
});

export type RegisterSystemInput = z.infer<typeof registerSystemSchema>;
