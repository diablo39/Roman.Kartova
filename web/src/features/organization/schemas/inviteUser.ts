import { z } from "zod";

/**
 * The four Kartova realm roles supported by the invitation flow. Mirrors the
 * backend `KartovaRoles.*` constants and the `CreateInvitationRequest.role`
 * wire enum (`Viewer | Member | TeamAdmin | OrgAdmin`). Listed as a `const`
 * tuple so `z.enum(KARTOVA_ROLES)` and `<select>` option rendering share a
 * single source of truth — adding a fifth role is a one-line change here.
 */
export const KARTOVA_ROLES = ["Viewer", "Member", "TeamAdmin", "OrgAdmin"] as const;
export type KartovaRole = (typeof KARTOVA_ROLES)[number];

/**
 * Validates user input for the "Invite user" dialog (slice-9 spec §6.7).
 *
 * Field rules:
 *  - `email` is required (no whitespace-only), max 320 characters (RFC 5321
 *    practical upper bound used by the C# handler), and must satisfy zod's
 *    built-in `z.string().email()` shape check. The server performs the
 *    canonical lowercase + length validation again and may still reject with
 *    422 (e.g. invalid TLD); that case is handled in the dialog with a toast.
 *  - `role` must be one of the four realm roles above. Anything else fails
 *    client-side before the network request.
 */
export const inviteUserSchema = z.object({
  email: z
    .string()
    .min(1, "Email is required.")
    .email("Invalid email address.")
    .max(320, "Email too long."),
  role: z.enum(KARTOVA_ROLES, { message: "Pick a role." }),
});

export type InviteUserInput = z.infer<typeof inviteUserSchema>;
