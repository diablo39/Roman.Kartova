# TypeScript / Node / React debugging cheatsheets

Concrete tooling for diagnosing hard TS/Node/React defects. Pinned versions are in
`knowledge/shared/versions.md`. Reproduce first, form one hypothesis, change one variable, confirm
against observed behaviour — do not guess-patch.

## Node inspector

```bash
node --inspect app.js            # attach a debugger, keep running
node --inspect-brk app.js        # break on the first line (debug startup)
node --inspect=0.0.0.0:9229 app.js  # remote / container debugging
# then open chrome://inspect, or attach from the editor
```

- TS entry: current Node LTS (`knowledge/shared/versions.md`) runs TypeScript directly via type
  stripping — `node --inspect app.ts` works for erasable-syntax TS with no loader. Use a loader
  (e.g. `node --inspect --import tsx app.ts`) only when the code needs non-erasable syntax
  (legacy enums, experimental decorators, path rewriting); either way breakpoints map to source.
- Editor auto-attach: enable "smart" auto-attach so `node` processes launched in the integrated
  terminal are debugged without a launch config.
- Long stack traces: `Error.stackTraceLimit = 50` and Chrome's "async stack traces" reveal the chain
  across `await` boundaries.

## Source maps

Breakpoints landing on the wrong line, or stack traces pointing at compiled JS, almost always mean a
source-map gap.

- Emit maps: `"sourceMap": true` (and `"declarationMap": true` for a library whose consumers debug
  into it). Bundlers need their source-map option on for dev.
- Common causes when maps are wrong: production build stripped maps, bundler `devtool`/source-map
  setting misconfigured, or a path-override mismatch between `webpack:///./src/*` and the real root.
- Verify the emitted `.map` sits next to the `.js` and its `sources` paths resolve.

## TypeScript compiler diagnostics

```bash
tsc --noEmit                       # type-check only (use this gate in CI)
tsc --noEmit --pretty false        # machine-readable errors
tsc --extendedDiagnostics          # phase timings + memory (why is checking slow?)
tsc --generateTrace ./trace        # emit a trace; open in a trace viewer for hotspots
tsc --traceResolution > res.log    # why a module/type resolves where it does
tsc --explainFiles                 # why each file is included in the program
```

Module-resolution failures: check `paths`/`baseUrl`, the package's `exports` map, and that the right
`@types/*` (or bundled types) are installed. Slow editor/type-check: `--extendedDiagnostics` and
`--generateTrace` locate the expensive type; usually a deep conditional/mapped type or a barrel
pulling the whole graph.

## React DevTools Profiler

1. Open React DevTools → Profiler, record, interact, stop.
2. Read the flame graph and the ranked chart: render duration, render count, and "why did this
   render?" (props/state/hooks change or parent re-render).
3. Highlight-updates (DevTools setting) paints re-rendering components on screen — the fastest way to
   spot a component re-rendering when its data did not change.

Inline `Profiler` for a specific subtree:

```tsx
import { Profiler } from "react";
<Profiler id="List" onRender={(id, phase, actual) => console.table({ id, phase, actual })}>
  <List />
</Profiler>
```

If the React Compiler is enabled (see `platform.md`), expect fewer manual-memoization wins; profile
to confirm a real re-render problem before changing code.

## Bundle analysis

```bash
# Vite / Rollup
npm i -D rollup-plugin-visualizer
#   vite.config.ts: plugins: [visualizer({ open: true, gzipSize: true })]

# webpack
npm i -D webpack-bundle-analyzer
#   plugin: new BundleAnalyzerPlugin({ analyzerMode: "static" })
```

Look for: a large dependency pulled in whole (import the submodule or a lighter alternative), the
same library duplicated at two versions (dedupe/align the range), moment/locale-style dead weight,
and anything server-only that leaked into the client chunk. Doubling as a security check, grep the
emitted client bundle for secret prefixes/values before shipping (`security.md#secrets`).

## Performance and Lighthouse

```bash
npm i -g @lhci/cli
lhci autorun --collect.url=http://localhost:3000
```

Core Web Vitals to watch: LCP (largest contentful paint), INP (interaction to next paint — the
responsiveness metric), CLS (layout shift); plus FCP and TBT in lab runs. Use the `performance`
marks API to measure a specific operation:

```ts
performance.mark("start");
// … work
performance.measure("op", "start");
console.table(performance.getEntriesByType("measure").map(m => ({ name: m.name, ms: m.duration })));
```

## Memory leaks

Browser: DevTools → Memory. Take a heap snapshot, interact, take another, compare retained size;
detached DOM nodes and growing arrays/maps point at the leak. Allocation-timeline recording shows
what is allocated and never freed.

Node:

```bash
node --inspect app.js            # DevTools → Memory → heap snapshot
node --heapsnapshot-signal=SIGUSR2 app.js   # write a snapshot on signal, no extra deps
node --cpu-prof / --heap-prof app.js        # built-in profiles, open in DevTools
```

(clinic.js is archived and unmaintained — rely on the built-in inspector, heap snapshots, and
profile flags above rather than adding it.)

Common React/Node leak sources (all fixed by a cleanup return / explicit release):

```ts
// listener / timer / socket opened in an effect with no cleanup
useEffect(() => {
  window.addEventListener("resize", onResize);
  return () => window.removeEventListener("resize", onResize); // <- the fix
}, []);
```

Others: `setInterval`/`setTimeout` never cleared, WebSocket/EventSource never closed, subscriptions
not unsubscribed, and closures/`Map` caches retaining large objects for their whole lifetime.

## Async and promises

```ts
// Surface swallowed rejections instead of losing them silently
process.on("unhandledRejection", (reason) => { console.error("unhandled", reason); process.exit(1); });
window.addEventListener("unhandledrejection", (e) => { console.error(e.reason); });
```

Race condition (overlapping loads writing shared state): dedupe with a single in-flight promise, or
let TanStack Query own the request. Cancel stale in-flight work with `AbortController` tied to effect
cleanup so a late response cannot set state after unmount.

## Network / CORS

```bash
# Reproduce a preflight and read the response headers
curl -i -X OPTIONS https://api.example.com/endpoint \
  -H "Origin: http://localhost:3000" \
  -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: Content-Type"
```

Check for `Access-Control-Allow-Origin` (matching the origin, not `*` when credentials are used),
`Access-Control-Allow-Methods`, `Access-Control-Allow-Headers`, and — for cookies —
`Access-Control-Allow-Credentials: true` with a specific origin. A failing preflight (non-2xx
OPTIONS) blocks the real request before it is sent.

## Dependency and build issues

```bash
npm ls typescript          # find duplicate/conflicting versions
npm ci                     # clean, lockfile-exact install to rule out drift
```

ESM/CJS interop errors, `optimizeDeps` mis-prebundling, and case-sensitive path failures (green on a
case-insensitive filesystem, red in CI) are the usual build-only culprits. Clear the tool cache and
reproduce in a clean checkout before blaming source.
