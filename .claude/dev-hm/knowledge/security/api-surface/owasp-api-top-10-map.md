# API surface protection — OWASP API Top 10 map

Section of `knowledge/security/api-surface.md`.


Where each risk's control lives. Depth stays in one file per family — this file covers the
surface-shape controls; identity controls have their own files.

| Risk (2023) | Control | Depth |
|---|---|---|
| API1 Broken object level authorization | Ownership check against the authenticated principal at every handler | `knowledge/security/authorization-design.md` |
| API2 Broken authentication | Session lifecycle, token verification, credential-flow hardening | `knowledge/security/authentication-sessions.md` |
| API3 Broken object property level authorization | Schema contracts: request property allowlists, response DTOs | this file, schema contracts |
| API4 Unrestricted resource consumption | Pagination caps, rate limits; work bounds | this file; `knowledge/security/resource-protection.md` |
| API5 Broken function level authorization | Deny-by-default route registration, role checks per operation | `knowledge/security/authorization-design.md` |
| API6 Unrestricted access to sensitive business flows | Per-principal quotas on sensitive flows; step-up verification | this file, rate limits; `knowledge/security/authentication-sessions.md` |
| API7 Server side request forgery | Request-target validation for server-initiated requests | `knowledge/security/secure-coding-review.md` (SSRF) |
| API8 Security misconfiguration | Response headers, CORS, error shape without internals | this file, error shape; `knowledge/security/browser-protections.md` |
| API9 Improper inventory management | Intentional route inventory, spec-versus-code verification | this file, inventory |
| API10 Unsafe consumption of APIs | Upstream responses treated as external input: schema-validate, bound, never trust | this file, schema contracts; `knowledge/security/secure-coding-review.md` (deserialization) |
