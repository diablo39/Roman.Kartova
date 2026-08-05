# TypeScript / web security patterns — Cross-site request forgery (CSRF) {#csrf}

Section of `knowledge/typescript/security.md`.


Cookie-based auth needs CSRF defence because the browser attaches the cookie to cross-site requests
automatically. Bearer tokens in an `Authorization` header are not auto-attached and are not exposed
to CSRF the same way (but see token storage above).

- `SameSite=Lax` (default) or `Strict` on session cookies blocks the common cross-site POST.
- For state-changing routes under cookie auth, also verify an anti-CSRF token (double-submit or
  synchronizer pattern) — every non-idempotent method (`POST`/`PUT`/`PATCH`/`DELETE`).
- Do not rely on checking `Origin`/`Referer` alone; treat it as one signal, not the control.
