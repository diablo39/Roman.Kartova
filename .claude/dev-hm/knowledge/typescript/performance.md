# Web and React performance

Optimization catalogue for TS/React apps: what to measure, then what to change. Profiling and
bundle-analysis workflows (how to find the problem) are in `debugging.md`; this file is what to do
about it. The current metric set and tool generations are in `knowledge/shared/versions.md`.
Optimize against a measurement, never speculatively — the review checklist grades speculative
memoization S3 for a reason.

## Metrics and budgets

Core Web Vitals (the "Web performance metrics" row in `versions.md`) are the user-facing targets,
assessed at the 75th percentile of real users:

| Metric | Measures | Good (p75) |
|---|---|---|
| LCP (Largest Contentful Paint) | loading — when the main content is visible | ≤ 2.5 s |
| INP (Interaction to Next Paint) | responsiveness — worst-case interaction latency | ≤ 200 ms |
| CLS (Cumulative Layout Shift) | visual stability — unexpected movement | ≤ 0.1 |

- Field data beats lab data: collect real-user metrics with the `web-vitals` library (report
  `onLCP`/`onINP`/`onCLS` to your telemetry) or read the Chrome UX Report; use Lighthouse/DevTools
  traces to reproduce and fix what the field data flags.
- Enforce bundle budgets in CI (`size-limit` or the bundler's report) per entry point, so a
  dependency that doubles the route bundle fails the PR instead of the p75.

## INP: keeping interactions under 200 ms

INP is dominated by long tasks on the main thread — anything over ~50 ms of uninterrupted JS delays
every pending interaction.

- Do less in the handler: update state, paint, then do the heavy derive after the frame. Break big
  synchronous loops with `scheduler.yield()` where available (fall back to `await new
  Promise(r => setTimeout(r))`) so input can interleave.
- Batch DOM reads and writes separately; interleaved read/write forces synchronous layout.
- Debounce high-frequency work (search-as-you-type network calls), but never the input echo itself.
- Third-party scripts are main-thread tenants: load them deferred/lazy, measure their long-task
  contribution, and evict what doesn't pay rent.
- CSS `content-visibility: auto` on long offscreen sections skips their render cost entirely.

## React rendering

Where the React Compiler is enabled (check `platform.md` and the project config), memoization is
automatic and hand `useMemo`/`useCallback`/`memo` is usually redundant. The techniques below matter
with or without the compiler because they change scheduling, not memoization.

- `useTransition`: mark expensive state updates as non-urgent so urgent ones (typing, clicking)
  interrupt them. The classic case is filtering a large list on each keystroke — the input echoes
  immediately, the list re-render is interruptible, `isPending` drives subtle feedback.
- `useDeferredValue`: same idea when you don't own the state update — defer the value a slow
  subtree consumes and let the rest of the UI stay current.
- Re-render storms (a context or store update re-rendering half the app) are diagnosed with the
  React DevTools Profiler (`debugging.md`) and fixed by narrowing subscriptions
  (`react-data-and-state.md`), not by sprinkling `memo`.

### Virtualization

Rendering thousands of rows costs mount time, memory, and every subsequent reconciliation; the DOM
only needs the visible window. Virtualize any list/table beyond a few hundred rows — TanStack
Virtual is the headless default:

```tsx
import { useVirtualizer } from "@tanstack/react-virtual";

function List({ rows }: { rows: Row[] }) {
  const parentRef = useRef<HTMLDivElement>(null);
  const v = useVirtualizer({
    count: rows.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 40,
    overscan: 10,
  });
  return (
    <div ref={parentRef} style={{ height: 600, overflow: "auto" }}>
      <div style={{ height: v.getTotalSize(), position: "relative" }}>
        {v.getVirtualItems().map((item) => (
          <div key={rows[item.index]!.id}   // stable domain key — QUA-TS-010
               style={{ position: "absolute", top: 0, transform: `translateY(${item.start}px)` }}>
            <RowView row={rows[item.index]!} />
          </div>
        ))}
      </div>
    </div>
  );
}
```

Pair with `useInfiniteQuery` + `maxPages` for feeds (`react-data-and-state.md`). Keys stay domain
ids — index keys break row state under scrolling reuse.

## Loading performance (LCP, CLS)

- Split at route boundaries: `React.lazy` + `Suspense` per route keeps the initial bundle to one
  route's code (`build-and-monorepo.md` for the mechanics). Prefetch the next route on intent
  (hover/viewport) rather than eagerly.
- Images: always set dimensions (or `aspect-ratio`) so late images don't shift layout (CLS);
  `loading="lazy"` below the fold, never on the LCP image — that one gets
  `fetchpriority="high"`/preload.
- Fonts: `font-display: swap`, preload the one or two weights the first paint needs, subset the
  files. A blocking webfont is a self-inflicted LCP penalty.
- Server side: stream the shell and defer slow data behind Suspense (`rsc-and-server-actions.md`);
  cache immutable assets aggressively at the CDN with hashed filenames.
- Avoid hydration mismatch warnings — each one is thrown-away server work re-rendered on the
  client.

## Node-side performance

Backend latency work is a different toolkit: event-loop hygiene, worker threads, and concurrency
limits live in `node-backend.md`; CPU/heap profiling workflows in `debugging.md`. The boundary
rule of thumb: fix p75 web vitals in this file, fix p99 API latency there.
