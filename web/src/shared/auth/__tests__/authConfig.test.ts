import { describe, it, expect } from "vitest";
import { buildOidcConfig } from "../authConfig";

describe("buildOidcConfig", () => {
  it("returns a UserManagerSettings shaped for PKCE + session-scoped storage", () => {
    const cfg = buildOidcConfig({
      authority: "http://kc/realms/kartova",
      clientId: "kartova-web",
      redirectUri: "http://localhost:5173/callback",
    });

    expect(cfg.authority).toBe("http://kc/realms/kartova");
    expect(cfg.client_id).toBe("kartova-web");
    expect(cfg.redirect_uri).toBe("http://localhost:5173/callback");
    expect(cfg.response_type).toBe("code");
    expect(cfg.scope).toContain("openid");
    expect(cfg.scope).toContain("profile");
    expect(cfg.automaticSilentRenew).toBe(true);
  });

  it("uses sessionStorage (not localStorage) so tokens are tab-scoped and cleared on tab close", () => {
    const cfg = buildOidcConfig({
      authority: "http://kc/realms/kartova",
      clientId: "kartova-web",
      redirectUri: "http://localhost:5173/callback",
    });

    expect(cfg.userStore).toBeDefined();
    expect(cfg.stateStore).toBeDefined();
    expect(cfg.userStore!.constructor.name).toBe("WebStorageStateStore");
    type WithStore = { _store?: Storage };
    const userStorage = (cfg.userStore as unknown as WithStore)._store;
    const stateStorage = (cfg.stateStore as unknown as WithStore)._store;
    expect(userStorage).toBe(window.sessionStorage);
    expect(stateStorage).toBe(window.sessionStorage);
    expect(userStorage).not.toBe(window.localStorage);
  });

  it("derives post_logout_redirect_uri from window.location.origin", () => {
    const cfg = buildOidcConfig({
      authority: "http://kc/realms/kartova",
      clientId: "kartova-web",
      redirectUri: "http://localhost:5173/callback",
    });
    expect(cfg.post_logout_redirect_uri).toBe(window.location.origin);
  });
});
