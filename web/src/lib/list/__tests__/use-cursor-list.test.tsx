import { describe, expect, it, vi } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useCursorList } from "../useCursorList";
import type { CursorPageEnvelope } from "../types";

function wrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

interface Row { id: string; name: string; }

function fetchPageMock(pages: CursorPageEnvelope<Row>[]) {
  return vi.fn(async (cursor: string | undefined) => {
    const idx = cursor ? Number(cursor) : 0;
    return pages[idx] ?? { items: [], nextCursor: null, prevCursor: null };
  });
}

describe("useCursorList", () => {
  it("loads first page on mount", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }, { id: "b", name: "B" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "c", name: "C" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.items.map(i => i.id)).toEqual(["a", "b"]);
    expect(result.current.hasNext).toBe(true);
    expect(result.current.hasPrev).toBe(false);
  });

  it("goNext advances and goPrev rewinds the cursor stack", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "b", name: "B" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => { result.current.goNext(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["b"]));
    expect(result.current.hasPrev).toBe(true);
    expect(result.current.hasNext).toBe(false);

    act(() => { result.current.goPrev(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["a"]));
    expect(result.current.hasPrev).toBe(false);
  });

  it("queryKey change resets stack synchronously without firing a stale-cursor request", async () => {
    // Regression test for the render-race fix: when queryKey identity changes
    // (e.g. user flips sort), the cursor stack must be reset before useQuery
    // is invoked, so the previous page's cursor never reaches fetchPage with
    // mismatched sort parameters. Previously the useEffect-based reset ran
    // *after* the render that detected the change, causing one wasted
    // fetchPage call with the stale cursor before the reset committed.
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "b", name: "B" }], nextCursor: null, prevCursor: null },
    ]);

    const { result, rerender } = renderHook(
      ({ key }: { key: string }) => useCursorList<Row>({ queryKey: ["t", key], fetchPage }),
      { wrapper: wrapper(qc), initialProps: { key: "a" } }
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => { result.current.goNext(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["b"]));
    expect(result.current.hasPrev).toBe(true);

    fetchPage.mockClear();
    rerender({ key: "b" });

    // After the rerender there must be exactly one fetch — for the first page
    // (cursor === undefined) — never the prior page's cursor "1".
    await waitFor(() => expect(fetchPage).toHaveBeenCalled());
    const cursorsRequested = fetchPage.mock.calls.map(call => call[0]);
    expect(cursorsRequested).toEqual([undefined]);
    expect(result.current.hasPrev).toBe(false);
  });

  it("reset clears the cursor stack and refetches first page", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "b", name: "B" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => { result.current.goNext(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["b"]));

    act(() => { result.current.reset(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["a"]));
    expect(result.current.hasPrev).toBe(false);
  });
});
