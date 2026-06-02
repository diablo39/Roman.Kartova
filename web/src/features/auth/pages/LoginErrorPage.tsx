import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";

import { Button } from "@/components/base/buttons/button";

/**
 * `/login-error` — terminal page shown when either the OIDC handshake or
 * the Kartova session bootstrap (`POST /api/v1/auth/session`) failed
 * (slice-9 spec §6). Offers two recovery paths:
 *
 *   - "Go home" — bounces back to `/`, which `<RequireAuth>` will turn into
 *     a fresh OIDC redirect once the user is signed out of the failed
 *     attempt.
 *   - "Try again" — kicks the OIDC redirect directly via
 *     `auth.signinRedirect()`.
 *
 * We deliberately do not show the underlying error message here — auth
 * failures often leak PII or sensitive headers; the developer-friendly
 * detail is already in `console.error` from the upstream `CallbackPage`.
 */
export function LoginErrorPage() {
  const navigate = useNavigate();
  const auth = useAuth();
  return (
    <div className="flex h-screen items-center justify-center bg-primary">
      <div className="max-w-md space-y-6 rounded-xl bg-secondary p-8 text-center shadow-lg">
        <h1 className="text-2xl font-semibold text-error-primary">
          Sign-in failed
        </h1>
        <p className="text-base text-tertiary">
          We couldn’t complete the sign-in. Please try again.
        </p>
        <div className="flex justify-center gap-3">
          <Button color="secondary" size="md" onClick={() => navigate("/")}>
            Go home
          </Button>
          <Button
            color="primary"
            size="md"
            onClick={() => void auth.signinRedirect()}
          >
            Try again
          </Button>
        </div>
      </div>
    </div>
  );
}
