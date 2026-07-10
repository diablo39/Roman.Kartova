import { describe, it, expect } from "vitest";
import { detectSpecKind } from "../detectSpecKind";

describe("detectSpecKind", () => {
  it("classifies JSON OpenAPI 3.x as openapi", () => {
    expect(detectSpecKind('{"openapi":"3.0.1","info":{}}', "application/json")).toBe("openapi");
  });
  it("classifies JSON Swagger 2.0 as openapi", () => {
    expect(detectSpecKind('{"swagger":"2.0","info":{}}', "application/json")).toBe("openapi");
  });
  it("classifies YAML openapi: as openapi", () => {
    expect(detectSpecKind("openapi: 3.1.0\ninfo:\n  title: X", "application/yaml")).toBe("openapi");
  });
  it("classifies YAML swagger: as openapi", () => {
    expect(detectSpecKind("swagger: '2.0'\ninfo: {}", "application/yaml")).toBe("openapi");
  });
  it("classifies AsyncAPI as other (out of scope this slice)", () => {
    expect(detectSpecKind("asyncapi: 3.0.0\nchannels: {}", "application/yaml")).toBe("other");
  });
  it("classifies GraphQL SDL as other", () => {
    expect(detectSpecKind("type Query { hello: String }", "text/plain")).toBe("other");
  });
  it("classifies arbitrary JSON without the key as other", () => {
    expect(detectSpecKind('{"foo":"bar"}', "application/json")).toBe("other");
  });
  it("classifies garbage / empty / null as other", () => {
    expect(detectSpecKind("not a spec at all", "text/plain")).toBe("other");
    expect(detectSpecKind("", "application/json")).toBe("other");
    expect(detectSpecKind(null)).toBe("other");
    expect(detectSpecKind(undefined)).toBe("other");
  });
  it("falls back to head-scan when JSON.parse fails but content is YAML openapi", () => {
    expect(detectSpecKind("openapi: 3.0.0\npaths: {}   # trailing", "application/json")).toBe("openapi");
  });
});
