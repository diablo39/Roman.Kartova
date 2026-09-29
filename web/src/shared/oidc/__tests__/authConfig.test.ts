import { describe, it, expect } from "vitest";
import { buildOidcConfig } from "../authConfig";

const baseInputs = {
  authority: "http://kc/realms/kartova",
  clientId: "kartova-web",
  redirectUri: "http://localhost:5173/callback",
  postLogoutRedirectUri: "http://localhost:5173",
  storage: window.sessionStorage,
};

describe("buildOidcConfig", () => {
  it("returns a UserManagerSettings shaped for PKCE + session-scoped storage", () => {
    const cfg = buildOidcConfig(baseInputs);

    expect(cfg.authority).toBe("http://kc/realms/kartova");
    expect(cfg.client_id).toBe("kartova-web");
    expect(cfg.redirect_uri).toBe("http://localhost:5173/callback");
    expect(cfg.response_type).toBe("code");
    expect(cfg.scope).toContain("openid");
    expect(cfg.scope).toContain("profile");
    expect(cfg.automaticSilentRenew).toBe(true);
  });

  it("threads the supplied storage into both userStore and stateStore (PKCE + token persistence)", () => {
    const cfg = buildOidcConfig(baseInputs);

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

  it("uses the supplied postLogoutRedirectUri verbatim", () => {
    const cfg = buildOidcConfig({
      ...baseInputs,
      postLogoutRedirectUri: "https://app.kartova.test",
    });
    expect(cfg.post_logout_redirect_uri).toBe("https://app.kartova.test");
  });
});
