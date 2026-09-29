import { afterEach, describe, expect, it } from "vitest";
import { resolveConfigValue } from "../runtimeConfig";

afterEach(() => {
  delete window.__KARTOVA_CONFIG__;
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
