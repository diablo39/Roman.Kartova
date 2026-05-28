interface CenteredSpinnerProps {
  message?: string;
}

/**
 * Full-viewport loading spinner used by auth-flow pages (OIDC callback,
 * pre-bootstrap shell loaders). Renders a centered animated SVG with a
 * caption underneath. `role="status"` + `aria-label` makes it discoverable
 * for assistive tech without requiring visible text duplication.
 */
export function CenteredSpinner({ message = "Loading…" }: CenteredSpinnerProps) {
  return (
    <div
      className="flex h-screen items-center justify-center"
      role="status"
      aria-label={message}
    >
      <div className="flex flex-col items-center gap-3">
        <svg
          fill="none"
          viewBox="0 0 24 24"
          className="size-8 animate-spin text-brand-secondary"
          aria-hidden="true"
        >
          <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
          />
          <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.4 0 0 5.4 0 12h4z"
          />
        </svg>
        <p className="text-sm text-tertiary">{message}</p>
      </div>
    </div>
  );
}
