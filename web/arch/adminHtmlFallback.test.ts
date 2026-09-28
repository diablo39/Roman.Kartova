import { describe, it, expect } from "vitest";
import { adminHtmlTarget } from "../vite-admin-html-fallback";

describe("adminHtmlTarget (ADR-0118)", () => {
  it("rewrites root navigation to /admin.html", () => {
    expect(adminHtmlTarget("GET", "/")).toBe("/admin.html");
  });

  it("rewrites the OIDC callback and preserves its query string", () => {
    expect(adminHtmlTarget("GET", "/callback?code=a&state=b")).toBe("/admin.html?code=a&state=b");
  });

  it("rewrites a deep client-routed path with no file extension", () => {
    expect(adminHtmlTarget("GET", "/organizations/xyz")).toBe("/admin.html");
  });

  it("rewrites an explicit /index.html request", () => {
    expect(adminHtmlTarget("GET", "/index.html")).toBe("/admin.html");
  });

  it("does not rewrite a built asset request", () => {
    expect(adminHtmlTarget("GET", "/assets/app-123.js")).toBeUndefined();
  });

  it("does not rewrite a raw admin source module request", () => {
    expect(adminHtmlTarget("GET", "/src/admin/main.tsx")).toBeUndefined();
  });

  it("does not rewrite a Vite-internal client request", () => {
    expect(adminHtmlTarget("GET", "/@vite/client")).toBeUndefined();
  });

  it("does not rewrite a node_modules dependency request", () => {
    expect(adminHtmlTarget("GET", "/node_modules/.vite/deps/react.js")).toBeUndefined();
  });

  it("does not rewrite a static file with an extension at the root", () => {
    expect(adminHtmlTarget("GET", "/vite.svg")).toBeUndefined();
  });

  it("does not rewrite a non-GET/HEAD request", () => {
    expect(adminHtmlTarget("POST", "/")).toBeUndefined();
  });
});
