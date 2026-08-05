# TypeScript platform: strict typing, React, Node, tooling

Pinned version numbers live in `knowledge/shared/versions.md`. This file covers practices and
patterns, and names a technology generation only where it defines the technique (React Server
Components, ESLint flat config, the MSW request-handler API). Check `versions.md` before pinning a
dependency.

## tsconfig baseline

Strict typing is the foundation every other check builds on. A production `tsconfig.json` turns on
the full strict family, not just `strict: true`:

```jsonc
{
  "compilerOptions": {
    "strict": true,                       // noImplicitAny, strictNullChecks, and the rest
    "noUncheckedIndexedAccess": true,     // arr[i] is T | undefined, not T
    "exactOptionalPropertyTypes": true,   // { a?: string } excludes explicit undefined
    "noImplicitOverride": true,
    "noFallthroughCasesInSwitch": true,
    "verbatimModuleSyntax": true,         // explicit `import type`; predictable emit
    "isolatedModules": true,              // safe for single-file transpilers
    "module": "nodenext",                 // or "preserve" for bundler-driven builds
    "moduleResolution": "nodenext",
    "target": "es2023"                    // example — set from the oldest supported runtime
                                          // (Node LTS row in knowledge/shared/versions.md)
  }
}
```

`noUncheckedIndexedAccess` and `exactOptionalPropertyTypes` catch the largest classes of real
runtime bugs and are off by default under `strict` — turn them on. The native (Go) compiler
generation changes build speed, not the type system; the same config applies.

## Type-safety patterns

### `unknown` over `any`

`any` disables checking and propagates silently; `unknown` forces a narrowing step before use. Every
value crossing a trust boundary (network, storage, `JSON.parse`, `catch`) starts as `unknown`.

```ts
// Avoid: any lets .toFixed() through with no check and infects callers.
function parse(json: string): any { return JSON.parse(json); }

// Prefer: unknown forces validation before the value is trusted.
function parse(json: string): unknown { return JSON.parse(json); }
const data = UserSchema.parse(parse(input)); // narrowed by a schema, see security.md
```

Catch clauses are `unknown` in strict mode — narrow before touching `.message`:

```ts
try { /* ... */ } catch (err) {
  if (err instanceof Error) log(err.message);
  else log(String(err));
}
```

### Discriminated unions over boolean flags and optional soup

Model states that cannot coexist as a union with a literal discriminant. Exhaustive `switch` with a
`never` default makes the compiler flag any unhandled case when the union grows.

```ts
type Result<T> =
  | { status: "loading" }
  | { status: "success"; data: T }
  | { status: "error"; error: Error };

function render<T>(r: Result<T>): string {
  switch (r.status) {
    case "loading": return "…";
    case "success": return String(r.data); // r.data exists only here
    case "error":   return r.error.message;
    default: {
      const _exhaustive: never = r; // compile error if a case is missed
      return _exhaustive;
    }
  }
}
```

This replaces the anti-pattern `{ isLoading: boolean; data?: T; error?: Error }`, where invalid
combinations (loading yet has data) are representable and every reader re-checks the same flags.

### Type guards, `satisfies`, and generics

- User-defined guards (`x is Foo`) narrow `unknown` locally; keep them next to the schema that backs
  them so the guard and the runtime check cannot drift.
- `satisfies` validates a literal against a type while keeping the narrow inferred type — use it for
  config objects instead of a widening annotation (`const config = {…} satisfies Config`).
- Constrain generics (`<T extends { id: string }>`) rather than accepting bare `T` and asserting
  inside. Return inferred types; annotate parameters.
- Prefer `readonly` arrays and `as const` for lookup tables so callers cannot mutate shared data.

Avoid enums for new code; use `as const` object maps or string-literal unions, which erase cleanly
under isolated/verbatim module modes and need no runtime object.

## React (function components, hooks, Server Components)

Baseline is function components with hooks. Review rules for hooks, effects, memoization, and
re-renders live in `review-checklist.md`; this section covers the current API surface.

- **Server Components (RSC)** render on the server and ship zero client JS for their own logic. Keep
  data fetching and secrets in Server Components; mark interactive leaves with `"use client"`. Never
  import a server-only module (DB client, secret reader) into a client component.
- **`use(promise)` / `use(context)`** reads a promise (suspends until resolved) or context. It may be
  called conditionally, unlike other hooks. Pair promise reads with a `<Suspense>` boundary and an
  error boundary.
- **Actions** are async functions wired to form submission or transitions. `useActionState` returns
  `[state, action, isPending]` (it replaced the earlier `useFormState` from `react-dom`).
  `useFormStatus` remains a separate hook: a child rendered inside a `<form>` reads the enclosing
  form's pending state without prop drilling. `useOptimistic` renders an optimistic value while the
  action is in flight.
- **The React Compiler** applies memoization automatically at build time. Where it is enabled, hand
  `useMemo`/`useCallback`/`memo` are usually redundant; do not add them speculatively. Where it is
  not enabled, memoize by the rules in `review-checklist.md`. Confirm which applies before reviewing
  memoization (`versions.md` records the compiler's status).
- Effects are for synchronizing with external systems, not for deriving state. Derive during render;
  reserve `useEffect` for subscriptions, timers, and imperative DOM/network work — always with a
  cleanup return.

## Node and ESM

- Author ESM (`"type": "module"`), `import`/`export` only. Use `node:` prefixed built-ins
  (`import { readFile } from "node:fs/promises"`). Avoid `require`, `__dirname`, and `__filename`;
  derive paths with `import.meta.url` / `import.meta.dirname`.
- Prefer the platform: `fetch`, `AbortController`, `structuredClone`, `node:test` where a full runner
  is overkill, and Web Streams are all built in on current LTS. Reach for a dependency only when the
  platform primitive is missing.
- Long-running work uses `AbortSignal` for cancellation and timeouts (`AbortSignal.timeout(ms)`).
  Register `process.on("unhandledRejection")` and `uncaughtException` handlers that log and exit
  non-zero rather than swallowing.

## Data layer and validation

- **Boundary validation**: parse every external payload (request body, query, env, message, third-
  party response) through a schema validator (Zod is the default) at the edge, then work with the
  inferred type inward. Details and the security rationale are in `security.md`.
- **ORMs**: both current TypeScript-first ORMs are viable — a schema-first client (Prisma) for
  ergonomics and migrations, or a code-first SQL builder (Drizzle) for smaller bundles and edge
  runtimes. Either gives compile-time-typed queries; do not hand-concatenate SQL (see `security.md`).
- **Server-state caching**: TanStack Query owns server state (fetching, caching, invalidation,
  `useSuspenseQuery` for Suspense integration). Keep it distinct from client UI state (a small store
  or plain context). Do not mirror server data into client state.

## Tooling

- **Build/dev**: Vite for apps and libraries; its Rollup-based production build and `esbuild` dev
  transform are the default. Bundle-analysis workflow is in `debugging.md`.
- **Lint**: ESLint flat config (`eslint.config.js`/`.ts`) is the only supported format on current
  majors — the legacy `.eslintrc` cascade is removed. Compose `@eslint/js` recommended,
  `typescript-eslint` recommended-type-checked, and `eslint-plugin-react-hooks` (its rules back the
  hooks checks in `review-checklist.md`). Formatting belongs to a formatter (Prettier/Biome), never
  to review prose.
- **Type-check in CI** separately from the bundler (`tsc --noEmit`); bundlers transpile without type
  checking, so a green build is not a green type-check.
