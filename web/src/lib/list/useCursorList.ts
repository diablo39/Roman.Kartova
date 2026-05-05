import { useCallback, useEffect, useRef, useState } from "react";
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
 * - Sort changes that mutate `queryKey` automatically reset the stack via
 *   useEffect dependency on the serialized key.
 */
export function useCursorList<TItem>(
  options: UseCursorListOptions<TItem>,
): CursorListResult<TItem> {
  const { queryKey, fetchPage, gcTime = 5 * 60 * 1000 } = options;
  const [stack, setStack] = useState<(string | undefined)[]>([undefined]);
  const currentCursor = stack[stack.length - 1];

  // Reset stack when queryKey identity changes (sort flip).
  const keyStr = JSON.stringify(queryKey);
  const prevKey = useRef(keyStr);
  useEffect(() => {
    if (prevKey.current !== keyStr) {
      prevKey.current = keyStr;
      setStack([undefined]);
    }
  }, [keyStr]);

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
    hasPrev: stack.length > 1,
    goNext,
    goPrev,
    reset,
  };
}
