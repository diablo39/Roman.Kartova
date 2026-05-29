import { toast } from "sonner";

import type { ProblemDetails } from "./problemDetails";

/**
 * Options for {@link toastProblem}. Each map is an opt-in dispatch table — keys
 * that match emit a `toast.error(message)`; unmatched errors fall through to
 * the next map (problem-type → status → fallback). Omit a map to skip that
 * dispatch entirely.
 */
export interface ToastProblemOptions {
  /**
   * Dispatch by RFC 7807 `type` URI. Keys are matched exactly OR by the
   * last URI segment ("tail") so callers can write either the canonical
   * `https://kartova.io/problems/email-already-in-tenant` or the shorthand
   * `email-already-in-tenant`. Compared in that order.
   */
  byProblemType?: Record<string, string>;
  /**
   * Dispatch by HTTP status (the `__status` field attached by
   * `throwWithStatus`). Numeric keys; first match wins. Use this for generic
   * 409 / 422 / 502 catch-alls when the `type` URI is absent or unrecognized.
   */
  byStatus?: Record<number, string>;
  /**
   * Surfaced when neither map matches. When omitted, the caller is signalled
   * (return value `false`) to handle the error itself — typically a more
   * specific path the helper doesn't know about.
   */
  fallback?: string;
}

/**
 * Maps an openapi-fetch error (with `__status` attached by `throwWithStatus`)
 * to a toast. Returns `true` when a toast was dispatched, `false` when nothing
 * matched and no fallback was supplied — in which case the caller should
 * handle the error.
 *
 * Centralizes the dispatch ladder that several feature dialogs had inline
 * (LogoUploader, InviteUserDialog). The match order is:
 *
 *   1. `byProblemType` — first by exact `type` URI, then by URI tail (last
 *      slash segment). Lets call sites pass either canonical URIs or short
 *      tokens without duplicating the prefix.
 *   2. `byStatus` — by the numeric `__status` attached by the helpers.
 *   3. `fallback` — extracts `problem.detail` or `problem.title` if present,
 *      otherwise emits the supplied string.
 */
export function toastProblem(
  err: unknown,
  opts: ToastProblemOptions = {},
): boolean {
  const problem = err as ProblemDetails & { __status?: number };

  if (opts.byProblemType && typeof problem?.type === "string") {
    const exact = opts.byProblemType[problem.type];
    if (exact !== undefined) {
      toast.error(exact);
      return true;
    }
    const tailIndex = problem.type.lastIndexOf("/");
    if (tailIndex >= 0 && tailIndex < problem.type.length - 1) {
      const tail = problem.type.substring(tailIndex + 1);
      const tailMatch = opts.byProblemType[tail];
      if (tailMatch !== undefined) {
        toast.error(tailMatch);
        return true;
      }
    }
  }

  if (opts.byStatus && typeof problem?.__status === "number") {
    const message = opts.byStatus[problem.__status];
    if (message !== undefined) {
      toast.error(message);
      return true;
    }
  }

  if (opts.fallback !== undefined) {
    const detail =
      typeof problem?.detail === "string" ? problem.detail : undefined;
    const title =
      typeof problem?.title === "string" ? problem.title : undefined;
    toast.error(detail ?? title ?? opts.fallback);
    return true;
  }

  return false;
}
