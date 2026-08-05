# TypeScript testing: Vitest, RTL, MSW, Playwright — Vitest

Section of `knowledge/typescript/testing.md`.


Default runner for unit and component tests (Vite-native, ESM-first, Jest-compatible API). Use the
`v8` coverage provider unless a gap needs Istanbul's precision.

```ts
// vitest.config.ts
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: "jsdom",           // "node" for backend-only suites
    setupFiles: "./src/test/setup.ts",
    coverage: {
      provider: "v8",
      reporter: ["text", "html"],
      thresholds: { branches: 80, functions: 80, lines: 80, statements: 80 },
    },
  },
});
```

Useful CLI: `vitest run` (CI, no watch), `vitest --coverage`, `vitest -t "name"` (filter),
`vitest related src/foo.ts` (only tests touching a file). Type-level tests use `expectTypeOf` /
`assertType` to assert on types, not just runtime values.

Micro-benchmarks live in `*.bench.ts` and run with `vitest bench` (`bench()` cases, `expect`
assertions on the summary); use them to catch regressions in hot pure functions, not as a substitute
for profiling a running app (`debugging.md`).
