import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * D3 (gate 8 deep-review, missing test 1): proves the runtime `/config.js` value (TD-016) actually
 * reaches each of the four consumer modules, not just the `resolveConfigValue` resolver in isolation
 * (runtimeConfig.test.ts). A consumer passing the wrong key, or reverting to a direct
 * `import.meta.env` read, would pass every other automated tier — only this test (and manual gate 9)
 * would catch it.
 */
const RUNTIME_CONFIG = {
  apiBaseUrl: "https://api.runtime",
  oidcAuthority: "https://kc.runtime/realms/x",
  oidcClientId: "rt-client",
};

afterEach(() => {
  delete window.__KARTOVA_CONFIG__;
  vi.restoreAllMocks();
});

describe("runtime config reaches its consumers", () => {
  it("@/features/catalog/api/client reads API_BASE_URL from the runtime config", async () => {
    vi.resetModules();
    window.__KARTOVA_CONFIG__ = RUNTIME_CONFIG;

    const { API_BASE_URL } = await import("@/features/catalog/api/client");

    expect(API_BASE_URL).toBe("https://api.runtime");
  });

  it("@/admin/api/client reads ADMIN_API_BASE_URL from the runtime config", async () => {
    vi.resetModules();
    window.__KARTOVA_CONFIG__ = RUNTIME_CONFIG;

    const { ADMIN_API_BASE_URL } = await import("@/admin/api/client");

    expect(ADMIN_API_BASE_URL).toBe("https://api.runtime");
  });

  it("@/shared/auth/AuthProvider builds its OIDC config from the runtime authority + client id", async () => {
    vi.resetModules();
    window.__KARTOVA_CONFIG__ = RUNTIME_CONFIG;
    const buildOidcConfig = vi.fn().mockReturnValue({});
    vi.doMock("@/shared/oidc/authConfig", () => ({ buildOidcConfig }));

    await import("@/shared/auth/AuthProvider");

    expect(buildOidcConfig).toHaveBeenCalledWith(
      expect.objectContaining({ authority: "https://kc.runtime/realms/x", clientId: "rt-client" }),
    );
  });

  it("@/admin/providers builds its OIDC config from the runtime authority + client id", async () => {
    vi.resetModules();
    window.__KARTOVA_CONFIG__ = RUNTIME_CONFIG;
    const buildOidcConfig = vi.fn().mockReturnValue({});
    vi.doMock("@/shared/oidc/authConfig", () => ({ buildOidcConfig }));

    await import("@/admin/providers");

    expect(buildOidcConfig).toHaveBeenCalledWith(
      expect.objectContaining({ authority: "https://kc.runtime/realms/x", clientId: "rt-client" }),
    );
  });
});
