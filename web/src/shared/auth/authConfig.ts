import type { UserManagerSettings } from "oidc-client-ts";
import { WebStorageStateStore, InMemoryWebStorage } from "oidc-client-ts";

export interface AuthConfigInputs {
  authority: string;
  clientId: string;
  redirectUri: string;
}

export function buildOidcConfig(i: AuthConfigInputs): UserManagerSettings {
  const memory = new InMemoryWebStorage();
  return {
    authority: i.authority,
    client_id: i.clientId,
    redirect_uri: i.redirectUri,
    post_logout_redirect_uri: window.location.origin,
    response_type: "code",
    scope: "openid profile email",
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: memory }),
    stateStore: new WebStorageStateStore({ store: memory }),
  };
}
