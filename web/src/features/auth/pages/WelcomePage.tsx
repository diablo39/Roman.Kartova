import { Navigate, useLocation, useNavigate } from "react-router-dom";

import { Button } from "@/components/base/buttons/button";
import type { components } from "@/generated/openapi";

type AcceptedInvitationInfo = components["schemas"]["AcceptedInvitationInfo"];

/**
 * `/welcome` — post-OIDC-callback celebration screen shown when the session
 * bootstrap auto-accepted an outstanding invitation in the same hop
 * (slice-9 spec §6). The router state is supplied by `OidcCallbackHandler`
 * via `navigate("/welcome", { state: r.acceptedInvitation })`.
 *
 * If a user lands here directly (refresh, bookmark, deep link) the router
 * state is empty — fall through to the catalog rather than render a broken
 * page.
 */
export function WelcomePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const info = location.state as AcceptedInvitationInfo | null;

  if (!info) return <Navigate to="/catalog" replace />;

  const inviterLabel = info.invitedBy.displayName || info.invitedBy.email;

  return (
    <div className="flex h-screen items-center justify-center bg-primary">
      <div className="max-w-md space-y-6 rounded-xl bg-secondary p-8 text-center shadow-lg">
        <h1 className="text-2xl font-semibold text-primary">
          Welcome to {info.orgDisplayName}!
        </h1>
        <p className="text-base text-tertiary">
          {inviterLabel} invited you to join.
        </p>
        <Button
          color="primary"
          size="md"
          onClick={() => navigate("/catalog", { replace: true })}
        >
          Continue to Kartova
        </Button>
      </div>
    </div>
  );
}
