# File upload handling — Pipeline overview

Section of `knowledge/security/file-upload-handling.md`.


Order matters: each stage runs before the next spends work or trust on the file, and a failure
at any stage refuses the upload with the validation event
(`knowledge/security/security-logging-detection.md#event-codes`, `upload_` family).

| Stage | Control | Backs |
|---|---|---|
| Accept | size cap enforced while streaming; count and rate caps; type allowlist | SEC-041, SEC-120 |
| Verify | magic bytes agree with the allowlisted type; a real parser validates the content | SEC-041 |
| Transform | images re-encoded; metadata stripped; active content refused or sanitized | SEC-003 |
| Store | server-generated non-guessable name; outside the web root, non-executable | SEC-041, SEC-045 |
| Serve | per-object authorization; stored content type; download-safe headers | SEC-011, SEC-043 |
