import { describe, it, expect } from "vitest";
import { detectSpecKind } from "../detectSpecKind";

describe("detectSpecKind", () => {
  it("classifies JSON OpenAPI 3.x as rendered", () => {
    expect(detectSpecKind('{"openapi":"3.0.1","info":{}}', "application/json")).toBe("rendered");
  });
  it("classifies JSON Swagger 2.0 as rendered", () => {
    expect(detectSpecKind('{"swagger":"2.0","info":{}}', "application/json")).toBe("rendered");
  });
  it("classifies JSON AsyncAPI 3.x as rendered", () => {
    expect(detectSpecKind('{"asyncapi":"3.0.0","info":{}}', "application/json")).toBe("rendered");
  });
  it("classifies YAML openapi: as rendered", () => {
    expect(detectSpecKind("openapi: 3.1.0\ninfo:\n  title: X", "application/yaml")).toBe("rendered");
  });
  it("classifies YAML swagger: as rendered", () => {
    expect(detectSpecKind("swagger: '2.0'\ninfo: {}", "application/yaml")).toBe("rendered");
  });
  it("classifies YAML asyncapi: as rendered (Scalar renders AsyncAPI too)", () => {
    expect(detectSpecKind("asyncapi: 3.0.0\nchannels: {}", "application/yaml")).toBe("rendered");
  });
  it("classifies GraphQL SDL as other", () => {
    expect(detectSpecKind("type Query { hello: String }", "text/plain")).toBe("other");
  });
  it("classifies arbitrary JSON without the key as other", () => {
    expect(detectSpecKind('{"foo":"bar"}', "application/json")).toBe("other");
  });
  it("does not false-positive on 'openapi' appearing in a JSON value, not a top-level key", () => {
    expect(detectSpecKind('{"description":"openapi is great","swaggering":true}', "application/json")).toBe("other");
  });
  it("classifies garbage / empty / null as other", () => {
    expect(detectSpecKind("not a spec at all", "text/plain")).toBe("other");
    expect(detectSpecKind("", "application/json")).toBe("other");
    expect(detectSpecKind(null)).toBe("other");
    expect(detectSpecKind(undefined)).toBe("other");
  });
  it("falls back to head-scan when JSON.parse fails but content is YAML openapi", () => {
    expect(detectSpecKind("openapi: 3.0.0\npaths: {}   # trailing", "application/json")).toBe("rendered");
  });
});
