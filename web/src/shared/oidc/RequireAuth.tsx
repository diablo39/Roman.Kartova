import { useEffect } from "react";
import { useAuth } from "react-oidc-context";
import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  // A failed signinRedirect (e.g. KeyCloak unreachable) resolves null, sets auth.error and closes the
  // navigator — auto-redirecting again would loop without bound. Show a retry panel instead. Other
  // error sources (e.g. a failed callback, LoginErrorPage "Go home") still get a fresh redirect.
  const redirectFailed = auth.error?.source === "signinRedirect" && !auth.isAuthenticated;

  // Round-trip the originally-requested URL (path + query + hash) through
  // the OIDC `state` so the post-login callback can restore the deep link
  // instead of dumping every user on /catalog (and dropping filter params).
  const signIn = () =>
    void auth.signinRedirect({
      state: {
        returnTo:
          window.location.pathname + window.location.search + window.location.hash,
      },
    });

  useEffect(() => {
    if (!redirectFailed && !auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
      signIn();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- signIn and redirectFailed derive from auth.
  }, [auth]);

  if (redirectFailed) {
    return (
      <CenteredMessage
        heading="Sign-in unavailable"
        body="The sign-in service could not be reached."
        action={
          <Button color="secondary" size="md" onClick={signIn}>
            Try again
          </Button>
        }
      />
    );
  }
  if (auth.isLoading || !auth.isAuthenticated) {
    return <div className="p-8 text-sm text-muted-foreground">Signing in…</div>;
  }
  return <>{children}</>;
}
