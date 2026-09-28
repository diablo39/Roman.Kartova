import type { Plugin } from "vite";

const VITE_INTERNAL_PREFIXES = ["/@", "/node_modules/", "/src/", "/__"];

/**
 * Decides whether an admin dev/preview server request should be rewritten to `/admin.html`.
 * Path-based, not Accept-based: a non-browser client (curl, a smoke/health tool) that omits
 * `Accept: text/html` must still land on the admin document, never Vite's default SPA fallback
 * (the tenant's root `web/index.html`) on the admin origin/port.
 *
 * Rewrites GET/HEAD requests (method `undefined` — e.g. Vite's own internal calls — is treated
 * as GET) whose path is `/index.html`, or whose last path segment has no file extension and does
 * not fall under a Vite-internal prefix (`/@…`, `/node_modules/…`, `/src/…`, `/__…`). Everything
 * else (built assets, raw source modules, non-GET/HEAD methods) is left alone. The original query
 * string is preserved on rewrite — the OIDC callback (`/callback?code=…&state=…`) needs it.
 */
export function adminHtmlTarget(method: string | undefined, url: string | undefined): string | undefined {
  if (method !== undefined && method.toUpperCase() !== "GET" && method.toUpperCase() !== "HEAD") return undefined;
  if (!url) return undefined;

  const withoutHash = url.split("#")[0]!;
  const queryIdx = withoutHash.indexOf("?");
  const path = queryIdx === -1 ? withoutHash : withoutHash.slice(0, queryIdx);
  const query = queryIdx === -1 ? "" : withoutHash.slice(queryIdx);

  if (path === "/index.html") return `/admin.html${query}`;
  if (VITE_INTERNAL_PREFIXES.some((prefix) => path.startsWith(prefix))) return undefined;

  const lastSegment = path.slice(path.lastIndexOf("/") + 1);
  const hasExtension = /\.[^./]+$/.test(lastSegment);
  if (hasExtension) return undefined;

  return `/admin.html${query}`;
}

/**
 * Serve admin.html for every path-eligible navigation on the admin dev/preview server
 * (path-based, not Accept-based — see `adminHtmlTarget`). Vite's SPA fallback would otherwise
 * serve the project-root index.html, i.e. the tenant app, on the admin origin. Registered
 * directly (not returned), so it runs before Vite's internal HTML middleware.
 */
export function adminHtmlFallback(): Plugin {
  const rewrite = (req: { url?: string; method?: string }) => {
    const target = adminHtmlTarget(req.method, req.url);
    if (target) req.url = target;
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
