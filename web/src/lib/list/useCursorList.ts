import { useCallback, useState } from "react";
import { useQuery, type QueryKey } from "@tanstack/react-query";
import type { CursorListResult, CursorPageEnvelope } from "./types";

interface UseCursorListOptions<TItem> {
  queryKey: QueryKey;
  fetchPage: (cursor: string | undefined) => Promise<CursorPageEnvelope<TItem>>;
  /** Garbage-collection time for cached pages (ms). Default 5 min. */
  gcTime?: number;
}

/**
 * Generic Prev/Next cursor-paginated list driver. Wraps TanStack Query's
 * useQuery (one query per page) and maintains an in-memory cursor stack so
 * "Prev" works without the server emitting prevCursor (ADR-0095 §5.2).
 *
 * - The cursor stack is `[undefined, c1, c2, ...]`; the last entry is the
 *   current page's cursor. goNext pushes the next-cursor; goPrev pops.
 * - Sort changes that mutate `queryKey` reset the stack synchronously during
 *   render (React's "store-previous-prop-in-state" pattern) so the stale
 *   cursor never reaches `useQuery` — otherwise a sort flip would emit one
 *   wasted 400 (cursor direction ≠ request order) before the reset committed.
 */
export function useCursorList<TItem>(
  options: UseCursorListOptions<TItem>,
): CursorListResult<TItem> {
  const { queryKey, fetchPage, gcTime = 5 * 60 * 1000 } = options;
  const keyStr = JSON.stringify(queryKey);
  const [stack, setStack] = useState<(string | undefined)[]>([undefined]);
  const [seenKey, setSeenKey] = useState(keyStr);

  // Synchronous reset during render: when `queryKey` identity changes (sort
  // flip), drop the cursor stack before `useQuery` reads `currentCursor`.
  // Setting state during render with a guard is the React-recommended way to
  // derive state from props without a render → useEffect → re-render race.
  let activeStack = stack;
  if (seenKey !== keyStr) {
    activeStack = [undefined];
    setStack(activeStack);
    setSeenKey(keyStr);
  }
  const currentCursor = activeStack[activeStack.length - 1];

  const query = useQuery({
    queryKey: [...queryKey, { cursor: currentCursor }],
    queryFn: () => fetchPage(currentCursor),
    gcTime,
  });

  const goNext = useCallback(() => {
    if (query.data?.nextCursor) {
      const next = query.data.nextCursor;
      setStack(prev => [...prev, next]);
    }
  }, [query.data]);

  const goPrev = useCallback(() => {
    setStack(prev => (prev.length > 1 ? prev.slice(0, -1) : prev));
  }, []);

  const reset = useCallback(() => setStack([undefined]), []);

  return {
    items: query.data?.items ?? [],
    isLoading: query.isLoading,
    isFetching: query.isFetching,
    isError: query.isError,
    hasNext: !!query.data?.nextCursor,
    hasPrev: activeStack.length > 1,
    goNext,
    goPrev,
    reset,
  };
}
