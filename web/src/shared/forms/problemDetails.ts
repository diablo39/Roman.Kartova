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
 * that is NOT in this set is deliberately skipped by `setError` — that call is a silent no-op on
 * an unregistered field — and, crucially, marks the result NOT fully handled so the caller still
 * shows its generic `toast.error`. Returns `true` only when at least one field was set AND **every**
 * error key mapped to a registered field. If any key is unmapped (including a payload that mixes a
 * mapped key with an unmapped one), returns `false`: the caller then both keeps the field
 * highlights that were set and shows a toast for the unmapped remainder, instead of silently
 * dropping it (TD-003 — the mixed-key case is the one a naive OR-fold would swallow).
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

  let appliedAny = false;
  let unmappedAny = false;
  for (const [field, messages] of Object.entries(errors)) {
    // A key that maps to no registered field can't be shown on the form — flag it so the caller
    // toasts instead of swallowing it, even when other keys DID map (the mixed-key case).
    if (!knownFields.has(field)) {
      unmappedAny = true;
      continue;
    }
    if (!Array.isArray(messages)) continue;
    for (const message of messages) {
      if (typeof message !== "string") continue;
      setError(field, { type: "server", message });
      appliedAny = true;
    }
  }
  return appliedAny && !unmappedAny;
}
