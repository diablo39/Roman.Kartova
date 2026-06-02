import { Link } from "react-router-dom";
import type { components } from "@/generated/openapi";

/**
 * Wire shape of an embedded owner reference (e.g. `ApplicationResponse.owner`).
 * Sourced directly from the OpenAPI codegen so a backend rename flows here
 * without a manual touch.
 */
type UserDisplayInfo = components["schemas"]["UserDisplayInfo"];

interface Props {
  /**
   * The owner's display info as embedded in the parent resource response.
   * May be `null` (resource exists but has no owner — e.g. deleted user) or
   * `undefined` (parent row is still loading).
   *
   * Both null and undefined render the same "Unknown user" fallback — the
   * caller can distinguish them at its own layer (skeleton vs. fallback) if
   * needed.
   */
  user: UserDisplayInfo | null | undefined;
}

/**
 * Pure link to a user's detail page (`/users/:id`). No data fetch — the parent
 * already carries the display info via the resource envelope (ADR-0098).
 *
 * Label preference: `displayName` first, then `email` as a fallback for users
 * whose display name hasn't been set yet (slice-9 spec §4.1).
 */
export function OwnerLink({ user }: Props) {
  if (!user) {
    return <span className="text-tertiary italic">Unknown user</span>;
  }
  return (
    <Link
      to={`/users/${user.id}`}
      className="font-medium text-primary hover:underline"
    >
      {user.displayName || user.email}
    </Link>
  );
}

export type { UserDisplayInfo };
