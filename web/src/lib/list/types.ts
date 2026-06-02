export type SortDirection = "asc" | "desc";

export interface CursorListResult<TItem> {
  items: TItem[];
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  /** The error value when isError is true; null otherwise. */
  error: unknown;
  hasNext: boolean;
  hasPrev: boolean;
  goNext: () => void;
  goPrev: () => void;
  reset: () => void;
  /**
   * Re-run the current page's query. Unlike `reset` (which only mutates the
   * cursor stack), `refetch` invalidates and re-runs the active query even
   * when the stack hasn't changed — required for retrying after a failed
   * first page, where `reset()` would be a no-op (stack is already
   * `[undefined]`).
   */
  refetch: () => void;
}

export interface CursorPageEnvelope<TItem> {
  items: TItem[];
  nextCursor: string | null;
  prevCursor: string | null;
}
