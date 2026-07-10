import { Component, type ErrorInfo, type ReactNode } from "react";
import { ApiReferenceReact } from "@scalar/api-reference-react";
import "@scalar/api-reference-react/style.css";

type Props = { content: string; mediaType: string; rawFallback: ReactNode };
type State = { failed: boolean };

/**
 * Encapsulates the Scalar OpenAPI renderer behind an error boundary. Any parse or
 * render failure degrades to the raw source (ADR-0084: never blank-page) AND is
 * logged (so the degrade is never silent). Consumers reset the boundary after a
 * failure by keying it on the spec content (`key={content}`), so a corrected or
 * replaced spec gets a fresh instance instead of a stuck fallback.
 * Default export so ApiSpecSection can React.lazy() it and code-split the bundle.
 */
export default class OpenApiRender extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // The UI silently degrades to raw otherwise; surface the failure so a Scalar
    // regression or a systematically-unrenderable spec is diagnosable. Log metadata
    // only (length/mediaType), never the full tenant-supplied content.
    console.error("[OpenApiRender] spec render failed; showing raw source", {
      mediaType: this.props.mediaType,
      contentLength: this.props.content.length,
      error,
      componentStack: info.componentStack,
    });
  }

  render() {
    if (this.state.failed) {
      return (
        <div className="space-y-2">
          <p className="text-sm text-warning-primary">Couldn't render this spec — showing source.</p>
          {this.props.rawFallback}
        </div>
      );
    }
    return (
      <div className="scalar-render overflow-auto rounded-md border border-secondary">
        <ApiReferenceReact
          configuration={{
            content: this.props.content,
            // Read-only: no live request execution this slice (spec §1.1, §6). Both keys
            // are required — hideClientButton hides the external-client/export button,
            // hideTestRequestButton hides the inline per-operation live "Test Request"
            // button (defaults to shown), which is the actual SSRF/live-request surface.
            hideClientButton: true,
            hideTestRequestButton: true,
            theme: "default",
          }}
        />
      </div>
    );
  }
}
