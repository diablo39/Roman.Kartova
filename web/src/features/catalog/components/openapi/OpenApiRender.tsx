import { Component, type ReactNode } from "react";
import { ApiReferenceReact } from "@scalar/api-reference-react";
import "@scalar/api-reference-react/style.css";

type Props = { content: string; mediaType: string; rawFallback: ReactNode };
type State = { failed: boolean };

/**
 * Encapsulates the Scalar OpenAPI renderer behind an error boundary. Any parse
 * or render failure degrades to the raw source (ADR-0084: never blank-page).
 * Default export so ApiSpecSection can React.lazy() it and code-split the bundle.
 */
export default class OpenApiRender extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
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
            // Read-only: no live request execution this slice (spec §1.1, §6).
            hideClientButton: true,
            theme: "default",
          }}
        />
      </div>
    );
  }
}
