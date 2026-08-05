# TypeScript / web security patterns — File upload handling {#file-upload}

Section of `knowledge/typescript/security.md`.


Uploads combine untrusted content, untrusted metadata (filename, declared type), and resource
consumption; each needs its own control.

- Enforce size and count limits in the multipart parser itself (`@fastify/multipart`/busboy
  `limits`), so an oversized part aborts mid-stream instead of after buffering. Stream uploads to
  disk or object storage — never assemble a whole file in memory (`node-backend.md`).
- Validate the type from content, not metadata: check magic bytes (a `file-type`-class detector)
  against an allowlist of expected types. The extension and the client's `Content-Type` header are
  attacker-chosen.
- Never use the client filename for storage — it is a path-traversal vector (`../../etc/cron.d/x`)
  and an injection vector in logs and headers. Generate the stored name (`crypto.randomUUID()` +
  a validated extension); keep the original, sanitized, as display metadata only.
- Store outside the web root (object storage bucket, dedicated volume). Serve downloads through a
  handler that authorizes per object — upload and download are separate authorization decisions —
  with `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, and the validated
  content type.
- SVG and HTML uploads execute script when served inline from your origin (stored XSS). Reject
  them unless they are a requirement; then serve them only as attachments or from a separate
  sandboxed origin with a restrictive CSP.
- Re-encode images where feasible (strips embedded payloads and EXIF location data). For archives,
  validate every entry path against zip-slip (`..`, absolute paths) and cap total expanded size and
  entry count before extraction.
