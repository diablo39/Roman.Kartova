import type { UserManagerSettings } from "oidc-client-ts";
import { WebStorageStateStore } from "oidc-client-ts";

export interface AuthConfigInputs {
  authority: string;
  clientId: string;
  redirectUri: string;
}

// Slice-4 §4.3: tokens are tab-scoped (sessionStorage) and cleared on tab close.
// In-memory storage is impossible for redirect-flow OIDC — the PKCE verifier
// must survive the navigation to the KeyCloak login page and back, which JS
// module memory does not. The harder security upgrade (BFF cookie session)
// is captured as backlog story E-01.F-04.S-05.
export function buildOidcConfig(i: AuthConfigInputs): UserManagerSettings {
  /* v8 ignore start -- SSR fallback; jsdom test env always has window */
  const store: Storage =
    typeof window !== "undefined"
      ? window.sessionStorage
      : ({
          length: 0,
          clear: () => {},
          getItem: () => null,
          key: () => null,
          removeItem: () => {},
          setItem: () => {},
        } as Storage);
  /* v8 ignore stop */
  return {
    authority: i.authority,
    client_id: i.clientId,
    redirect_uri: i.redirectUri,
    post_logout_redirect_uri:
      typeof window !== "undefined" ? window.location.origin : "/",
    response_type: "code",
    scope: "openid profile email",
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store }),
    stateStore: new WebStorageStateStore({ store }),
  };
}
