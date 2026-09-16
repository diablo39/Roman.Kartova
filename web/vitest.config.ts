import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "node:path";

// ESM requires `import.meta.dirname` (Node 20.11+) — `__dirname` is undefined in ESM modules.
const dirname = import.meta.dirname;

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { "@": path.resolve(dirname, "src") } },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    // TD-008: the full suite intermittently times out 1-3 tests (seen in ServiceDetailPage /
    // ApplicationDetailPage "not-found" cases) under parallel load — heavy per-file barrel-import
    // + jsdom-environment cost (cumulative import ~1900s / environment ~1300s across workers)
    // starves individual tests of the default 5s budget. They pass fast in isolation. Mitigation:
    // give each test more headroom AND cap fork concurrency so the import storm is less severe
    // (trades some wall-clock for determinism). Root import-cost reduction is deferred.
    testTimeout: 15000,
    hookTimeout: 15000,
    maxWorkers: "50%",
    coverage: {
      provider: "v8",
      include: [
        "src/features/**/api/**",
        "src/features/**/schemas/**",
        "src/shared/auth/**",
        "src/shared/forms/**",
      ],
      exclude: [
        // Composition-root wiring (analogous to Program.cs); behavior covered via authConfig.
        "src/shared/auth/AuthProvider.tsx",
      ],
      thresholds: { lines: 80, statements: 80, functions: 80, branches: 75 },
    },
  },
});
