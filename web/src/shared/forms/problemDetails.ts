export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

type SetError = (
  name: string,
  error: { type: string; message: string }
) => void;

export function applyProblemDetailsToForm(
  payload: ProblemDetails | null | undefined,
  setError: SetError
): boolean {
  if (!payload || typeof payload !== "object") return false;
  const errors = payload.errors;
  if (!errors || typeof errors !== "object") return false;

  let any = false;
  for (const [field, messages] of Object.entries(errors)) {
    if (!Array.isArray(messages)) continue;
    for (const message of messages) {
      if (typeof message !== "string") continue;
      setError(field, { type: "server", message });
      any = true;
    }
  }
  return any;
}
