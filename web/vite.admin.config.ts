import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

// ESM requires `import.meta.dirname` (Node 20.11+) — `__dirname` is undefined in ESM modules.
const dirname = import.meta.dirname;

/**
 * Serve admin.html for every HTML navigation on the admin dev/preview server. Vite's SPA fallback
 * would otherwise serve the project-root index.html, i.e. the tenant app, on the admin origin.
 * Registered directly (not returned), so it runs before Vite's internal HTML middleware.
 */
function adminHtmlFallback(): Plugin {
  const rewrite = (req: { url?: string; headers: { accept?: string } }) => {
    if (req.url && req.headers.accept?.includes("text/html")) req.url = "/admin.html";
  };
  return {
    name: "kartova-admin-html-fallback",
    configureServer(server) {
      server.middlewares.use((req, _res, next) => { rewrite(req); next(); });
    },
    configurePreviewServer(server) {
      server.middlewares.use((req, _res, next) => { rewrite(req); next(); });
    },
  };
}

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
