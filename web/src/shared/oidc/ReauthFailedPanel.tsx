import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";

/**
 * Terminal state of the TD-017 re-auth circuit breaker: SSO succeeded but the API keeps answering 401
 * (stale/rejected token, clock skew, misconfigured realm). Replaces the whole app — shared by the tenant
 * SPA and the admin console.
 */
export function ReauthFailedPanel({ onRetry, onSignOut }: { onRetry: () => void; onSignOut: () => void }) {
  return (
    <CenteredMessage
      heading="Signed in, but the session was rejected"
      body="The API refused the new sign-in. Try again, or sign out and sign in with a different account."
      action={
        <div className="flex justify-center gap-3">
          <Button color="primary" size="md" onClick={onRetry}>
            Try again
          </Button>
          <Button color="secondary" size="md" onClick={onSignOut}>
            Sign out
          </Button>
        </div>
      }
    />
  );
}
