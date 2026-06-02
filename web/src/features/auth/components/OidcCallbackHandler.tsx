import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";

import { useStartSession } from "@/features/auth/api/session";
import { CenteredSpinner } from "@/features/auth/components/CenteredSpinner";
import { orgKeys } from "@/features/organization/api/organization";

/**
 * Runs the Kartova session-bootstrap step that follows a successful OIDC
 * callback (slice-9 spec §6). Once the OIDC provider has returned an access
 * token (handled by the outer `<CallbackPage />`), this component:
 *
 *   1. POSTs `/api/v1/auth/session` to exchange the token for a Kartova
 *      session payload (role, permissions, organization snapshot, etc).
 *   2. Pre-populates `orgKeys.profile()` from the response so the next
 *      `useOrgProfile()` hit (in the shell, tenant pill, etc.) is a cache
 *      read and skips a roundtrip.
 *   3. If the backend auto-accepted an outstanding invitation in the same
 *      hop (`acceptedInvitation != null`), routes to `/welcome` with the
 *      celebration payload as router state.
 *   4. Otherwise routes to `/catalog`.
 *   5. On failure routes to `/login-error`.
 *
 * The `mounted` flag guards against React Strict-Mode double-invocation
 * and unmount-during-mutation — late resolution must not call
 * `setQueryData` / `navigate` after the consumer left the route.
 */
export function OidcCallbackHandler() {
  const navigate = useNavigate();
  const startSession = useStartSession();
  const qc = useQueryClient();

  useEffect(() => {
    let mounted = true;
    void (async () => {
      try {
        const r = await startSession.mutateAsync();
        if (!mounted) return;
        qc.setQueryData(orgKeys.profile(), r.organization);
        if (r.acceptedInvitation) {
          navigate("/welcome", {
            state: r.acceptedInvitation,
            replace: true,
          });
        } else {
          navigate("/catalog", { replace: true });
        }
      } catch {
        if (!mounted) return;
        navigate("/login-error", { replace: true });
      }
    })();
    return () => {
      mounted = false;
    };
    // mutateAsync / qc / navigate are stable refs for the lifetime of the
    // component and we only want this effect to run once on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return <CenteredSpinner message="Signing you in…" />;
}
