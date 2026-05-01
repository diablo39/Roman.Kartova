import type { UserManagerSettings } from "oidc-client-ts";
import { WebStorageStateStore } from "oidc-client-ts";

export interface AuthConfigInputs {
  authority: string;
  clientId: string;
  redirectUri: string;
  postLogoutRedirectUri: string;
  storage: Storage;
}

// Slice-4 §4.3: tokens are tab-scoped (sessionStorage) and cleared on tab close.
// In-memory storage is impossible for redirect-flow OIDC — the PKCE verifier
// must survive the navigation to the KeyCloak login page and back, which JS
// module memory does not. The harder security upgrade (BFF cookie session)
// is captured as backlog story E-01.F-04.S-05.
//
// This function is pure: it takes a Storage and origin string from the caller
// instead of touching `window`. The composition root (AuthProvider.tsx) supplies
// `window.sessionStorage` and `window.location.origin`.
export function buildOidcConfig(i: AuthConfigInputs): UserManagerSettings {
  return {
    authority: i.authority,
    client_id: i.clientId,
    redirect_uri: i.redirectUri,
    post_logout_redirect_uri: i.postLogoutRedirectUri,
    response_type: "code",
    scope: "openid profile email",
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: i.storage }),
    stateStore: new WebStorageStateStore({ store: i.storage }),
  };
}
