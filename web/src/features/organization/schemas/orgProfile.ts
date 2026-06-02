import { z } from "zod";

/**
 * Deprecated-but-still-accepted UTC aliases. The C# backend treats these as
 * valid (via `TimeZoneInfo.TryFindSystemTimeZoneById`) and seeds new orgs
 * with `"UTC"`, so the SPA schema must accept them even though they are not
 * in `Intl.supportedValuesOf("timeZone")`.
 */
const UTC_ALIASES: ReadonlySet<string> = new Set(["UTC", "Etc/UTC", "Etc/GMT"]);

/**
 * Validates user input for editing the current organization's profile
 * (`PUT /api/v1/organizations/me`, slice-9 spec §4).
 *
 * Field rules mirror the established `editApplication.ts` pattern:
 *
 *  - `displayName` is required, 1-128 chars, and rejects whitespace-only.
 *  - `description` is optional and may be `null` or an empty string — the
 *    API treats `""` and `null` as the same "no description" state, and
 *    `OrgProfileResponse.description` itself is `null | string`. We accept
 *    empty strings as-is and let the page collapse `""` → `null` at submit
 *    time if it wants to send the canonical null form. We DO enforce a
 *    1024-char ceiling so very long descriptions are rejected client-side
 *    before the server has to.
 *  - `defaultTimeZone` must be a valid IANA zone the runtime knows about.
 *    `Intl.supportedValuesOf("timeZone")` returns the host platform's list
 *    (Node 18+, modern browsers); we filter rather than trust the input
 *    because a stale frontend could otherwise post an unknown zone that
 *    the server then has to reject with a 400.
 */
export const orgProfileSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display name is required.")
    .max(128, "Display name must be 128 characters or fewer.")
    .refine((v) => v.trim().length > 0, {
      message: "Display name must not be only whitespace.",
    }),
  description: z
    .string()
    .max(1024, "Description must be 1024 characters or fewer.")
    .nullable()
    .optional(),
  defaultTimeZone: z
    .string()
    .min(1, "Default time zone is required.")
    .refine(
      (tz) =>
        // Intl.supportedValuesOf("timeZone") excludes deprecated aliases such
        // as "UTC" / "Etc/UTC" / "Etc/GMT" (only canonical zones are listed),
        // but the C# backend uses `TimeZoneInfo.TryFindSystemTimeZoneById`,
        // which accepts those aliases — and the seeded organization default
        // is `"UTC"`. Whitelist the aliases explicitly so a freshly-onboarded
        // tenant doesn't get a phantom "Unknown IANA time-zone" error against
        // the value the server itself returned.
        UTC_ALIASES.has(tz) || Intl.supportedValuesOf("timeZone").includes(tz),
      { message: "Unknown IANA time-zone." },
    ),
});

export type OrgProfileInput = z.infer<typeof orgProfileSchema>;
