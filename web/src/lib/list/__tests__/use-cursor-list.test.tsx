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
