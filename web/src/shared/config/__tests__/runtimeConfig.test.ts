import { afterEach, describe, expect, it, vi } from "vitest";
import { resetRuntimeConfigWarningsForTests, resolveConfigValue } from "../runtimeConfig";

afterEach(() => {
  delete window.__KARTOVA_CONFIG__;
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
  resetRuntimeConfigWarningsForTests();
});

describe("resolveConfigValue", () => {
  it("prefers a non-empty runtime value over VITE_* and the default", () => {
    window.__KARTOVA_CONFIG__ = { apiBaseUrl: "https://api.runtime" };
    expect(resolveConfigValue("apiBaseUrl", "https://api.vite", "http://localhost:8080")).toBe("https://api.runtime");
  });

  it("treats an empty runtime value (unset container env) as absent and uses VITE_*", () => {
    window.__KARTOVA_CONFIG__ = { apiBaseUrl: "" };
    expect(resolveConfigValue("apiBaseUrl", "https://api.vite", "http://localhost:8080")).toBe("https://api.vite");
  });

  it("falls back to the default when runtime and VITE_* are both empty or missing", () => {
    window.__KARTOVA_CONFIG__ = { oidcClientId: "" };
    expect(resolveConfigValue("oidcClientId", "", "kartova-web")).toBe("kartova-web");
    delete window.__KARTOVA_CONFIG__;
    expect(resolveConfigValue("oidcClientId", undefined, "kartova-web")).toBe("kartova-web");
  });

  it("reads each key independently", () => {
    window.__KARTOVA_CONFIG__ = { oidcAuthority: "https://kc.runtime/realms/r" };
    expect(resolveConfigValue("oidcAuthority", undefined, "d")).toBe("https://kc.runtime/realms/r");
    expect(resolveConfigValue("apiBaseUrl", undefined, "http://localhost:8080")).toBe("http://localhost:8080");
  });
});

describe("resolveConfigValue production fallback warning", () => {
  it("warns once (naming the env var) when PROD and falling back, and not again for the same key", () => {
    vi.stubEnv("PROD", true);
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

    expect(resolveConfigValue("apiBaseUrl", undefined, "http://localhost:8080")).toBe("http://localhost:8080");
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0]?.[0]).toContain("KARTOVA_API_BASE_URL");

    expect(resolveConfigValue("apiBaseUrl", undefined, "http://localhost:8080")).toBe("http://localhost:8080");
    expect(warnSpy).toHaveBeenCalledTimes(1);
  });

  it("does not warn when PROD and a runtime value is present", () => {
    vi.stubEnv("PROD", true);
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    window.__KARTOVA_CONFIG__ = { oidcClientId: "runtime-client" };

    expect(resolveConfigValue("oidcClientId", undefined, "kartova-web")).toBe("runtime-client");
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it("does not warn when falling back outside PROD (dev/test)", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

    expect(resolveConfigValue("oidcAuthority", undefined, "http://localhost:8081")).toBe("http://localhost:8081");
    expect(warnSpy).not.toHaveBeenCalled();
  });
});
