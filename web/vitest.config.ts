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
