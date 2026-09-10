export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

/**
 * Runtime guard for a value that is claimed to be a `ProblemDetails` body (e.g. from a caught
 * fetch error whose type is `unknown`). Every field on `ProblemDetails` is optional, so this
 * intentionally only checks the shape that all real usages need: a plain object, since RFC 7807
 * bodies are always JSON objects (never arrays, primitives, or null).
 */
export function asProblemDetails(value: unknown): ProblemDetails | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return undefined;
  return value as ProblemDetails;
}

type SetError = (
  name: string,
  error: { type: string; message: string }
) => void;

/**
 * Applies a server 400 `ProblemDetails.errors` map onto a form via `setError`, returning whether
 * anything was applied to a REAL, registered form field.
 *
 * `knownFields` is the set of registered field paths (see {@link zodFieldPaths}). An error key
 * that is NOT in this set is deliberately skipped — `setError` on an unregistered field is a
 * silent no-op, so counting it as "handled" would let a 400 vanish with no toast and no field
 * highlight. Skipped keys leave `handled = false` (when no key mapped), so the caller falls
 * through to its generic `toast.error` fallback instead of swallowing the error (TD-003).
 *
 * Pass an empty set for a form with no fields (e.g. a confirm dialog): every error key is then
 * unmapped and the caller always shows a toast.
 */
export function applyProblemDetailsToForm(
  payload: ProblemDetails | null | undefined,
  setError: SetError,
  knownFields: ReadonlySet<string>
): boolean {
  if (!payload || typeof payload !== "object") return false;
  const errors = payload.errors;
  if (!errors || typeof errors !== "object") return false;

  let any = false;
  for (const [field, messages] of Object.entries(errors)) {
    if (!Array.isArray(messages)) continue;
    // An error key that maps to no registered field must not count as handled — otherwise the
    // caller skips its toast and the 400 is silently swallowed (TD-003).
    if (!knownFields.has(field)) continue;
    for (const message of messages) {
      if (typeof message !== "string") continue;
      setError(field, { type: "server", message });
      any = true;
    }
  }
  return any;
}
