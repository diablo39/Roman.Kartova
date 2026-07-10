import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import OpenApiRender from "../OpenApiRender";

// Scalar is heavy/web-component-ish; mock it. First test: it throws → boundary catches.
vi.mock("@scalar/api-reference-react", () => ({
  ApiReferenceReact: () => {
    throw new Error("scalar boom");
  },
}));

describe("OpenApiRender", () => {
  it("falls back to raw source + notice when the renderer throws", () => {
    render(
      <OpenApiRender content="openapi: 3.0.0" mediaType="application/yaml" rawFallback={<pre>RAW-SOURCE</pre>} />,
    );
    expect(screen.getByText("RAW-SOURCE")).toBeInTheDocument();
    expect(screen.getByText(/couldn't render/i)).toBeInTheDocument();
  });
});
