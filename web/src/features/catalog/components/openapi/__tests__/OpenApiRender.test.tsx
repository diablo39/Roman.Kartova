import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import OpenApiRender from "../OpenApiRender";

// Scalar is heavy/web-component-ish; mock it. Throws for content containing "boom"
// (to exercise the boundary), renders a probe otherwise (to assert the config shape).
vi.mock("@scalar/api-reference-react", () => ({
  ApiReferenceReact: ({
    configuration,
  }: {
    configuration: { content: string; hideClientButton?: boolean; hideTestRequestButton?: boolean };
  }) => {
    if (configuration.content.includes("boom")) throw new Error("scalar boom");
    return (
      <div
        data-testid="scalar-ok"
        data-content={configuration.content}
        data-hide-client={String(configuration.hideClientButton)}
        data-hide-test={String(configuration.hideTestRequestButton)}
      />
    );
  },
}));

describe("OpenApiRender", () => {
  it("renders Scalar read-only (hideClientButton) with the spec content on the happy path", () => {
    render(<OpenApiRender content="openapi: 3.0.0" mediaType="application/yaml" rawFallback={<pre>RAW</pre>} />);
    const el = screen.getByTestId("scalar-ok");
    // Locks the read-only intent: a future edit dropping either flag fails here.
    // hideTestRequestButton is the one that disables live request execution.
    expect(el).toHaveAttribute("data-hide-client", "true");
    expect(el).toHaveAttribute("data-hide-test", "true");
    expect(el).toHaveAttribute("data-content", "openapi: 3.0.0");
  });

  it("falls back to raw source + notice when the renderer throws", () => {
    render(<OpenApiRender content="boom" mediaType="application/yaml" rawFallback={<pre>RAW-SOURCE</pre>} />);
    expect(screen.getByText("RAW-SOURCE")).toBeInTheDocument();
    expect(screen.getByText(/couldn't render/i)).toBeInTheDocument();
  });

  it("a keyed remount on new content clears a prior failure (consumer resets via key)", () => {
    // Mirrors ApiSpecSection's `key={content}`: after a spec fails, replacing it with
    // a good spec (new key) remounts a fresh boundary instead of leaving it stuck.
    const { rerender } = render(
      <OpenApiRender key="boom" content="boom" mediaType="application/yaml" rawFallback={<pre>RAW-SOURCE</pre>} />,
    );
    expect(screen.getByText(/couldn't render/i)).toBeInTheDocument();
    rerender(
      <OpenApiRender key="good" content="openapi: 3.0.0" mediaType="application/yaml" rawFallback={<pre>RAW-SOURCE</pre>} />,
    );
    expect(screen.getByTestId("scalar-ok")).toBeInTheDocument();
    expect(screen.queryByText(/couldn't render/i)).not.toBeInTheDocument();
  });
});
