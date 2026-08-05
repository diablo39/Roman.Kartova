# TypeScript / web security patterns — Node transport and headers {#node}

Section of `knowledge/typescript/security.md`.


- Enforce HTTPS/TLS at the edge; send HSTS. Set security headers (CSP, `X-Content-Type-Options`,
  `Referrer-Policy`, frame-ancestors) via middleware.
- Cap request body size and apply rate limiting on auth and mutation endpoints.
- Log auth failures and validation rejections without logging the offending secret/token value.
