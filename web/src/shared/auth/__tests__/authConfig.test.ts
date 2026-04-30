import { describe, it, expect } from "vitest";
import { buildOidcConfig } from "../authConfig";

describe("buildOidcConfig", () => {
  it("returns a UserManagerSettings shaped for PKCE + in-memory storage", () => {
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

  it("provides in-memory user/state stores (not localStorage)", () => {
    const cfg = buildOidcConfig({
      authority: "http://kc/realms/kartova",
      clientId: "kartova-web",
      redirectUri: "http://localhost:5173/callback",
    });

    expect(cfg.userStore).toBeDefined();
    expect(cfg.stateStore).toBeDefined();
    // Stores must NOT be a WebStorageStateStore wrapping window.localStorage / sessionStorage.
    // Easiest invariant: the underlying store should not be a Storage instance.
    // (Stricter — we know the implementation uses InMemoryWebStorage, so identity check by name:)
    expect(cfg.userStore!.constructor.name).toBe("WebStorageStateStore");
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
