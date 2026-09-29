import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";
import { adminHtmlFallback } from "./vite-admin-html-fallback";

// ESM requires `import.meta.dirname` (Node 20.11+) — `__dirname` is undefined in ESM modules.
const dirname = import.meta.dirname;

// ADR-0118 (amended 2026-09-28): the platform-operator console is a second entry of this project with its
// own build output, image, CSP and origin. Only admin.html is a build input, so tenant pages never enter
// this bundle (guarded by arch/adminImportBoundary.test.ts).
export default defineConfig({
  plugins: [react(), tailwindcss(), adminHtmlFallback()],
  resolve: {
    alias: {
      "@": path.resolve(dirname, "./src"),
    },
  },
  build: {
    outDir: "dist-admin",
    rollupOptions: { input: path.resolve(dirname, "admin.html") },
  },
  server: { port: 5174, strictPort: true },
  preview: { port: 4174, strictPort: true },
});
