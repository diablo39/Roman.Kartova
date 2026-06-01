import { z } from "zod";

/**
 * Validates user input for the "Accept invitation" form (password set + display name).
 *
 * Field rules:
 *  - `password` is required, 12–128 characters (mirrors backend C# handler validation).
 *  - `confirmPassword` must match `password` exactly.
 *  - `displayName` is required, trimmed, max 128 characters.
 */
export const acceptInvitationSchema = z
  .object({
    password: z
      .string()
      .min(12, "Password must be at least 12 characters.")
      .max(128, "Password too long."),
    confirmPassword: z.string(),
    displayName: z
      .string()
      .trim()
      .min(1, "Display name is required.")
      .max(128, "Display name too long."),
  })
  .refine((v) => v.password === v.confirmPassword, {
    message: "Passwords do not match.",
    path: ["confirmPassword"],
  });

export type AcceptInvitationInput = z.infer<typeof acceptInvitationSchema>;
