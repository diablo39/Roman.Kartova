# Security oracle — Input validation and binding

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-040 | Boundary validation | Every new external input (parameter, body, header, file, message) is validated for type, length, range, or schema before first use; failures reject the request rather than silently continuing | S1 | CWE-20 · V2 | knowledge/security/secure-coding-review/input-validation.md |
| SEC-041 | File upload constraints | Uploaded files are checked for size and type; stored under generated names outside the web root or in non-executable storage; the client-supplied filename is never used as a path | S1 | CWE-434 · V5 | knowledge/security/secure-coding-review/files-and-paths.md |
| SEC-042 | Redirect allowlist | Redirect and forward targets derived from external input match an allowlist or are relative paths; zero open redirects | S2 | CWE-601 · V2 | knowledge/security/secure-coding-review/input-validation.md |
| SEC-043 | Downstream encoding | Values written into response headers, CSV, or other downstream contexts are encoded for that context; CR/LF stripped from header values; spreadsheet-formula prefixes escaped in CSV exports | S2 | CWE-113, CWE-1236 · V1 | knowledge/security/secure-coding-review/output-encoding-and-xss.md |
| SEC-044 | Mass assignment blocked | Request-to-object binding uses an explicit field allowlist or a DTO limited to client-settable fields; zero bind-all/auto-bind onto objects carrying privileged fields (role, price, owner, verified) | S1 | A01 · CWE-915 · V2 · API3 | knowledge/security/secure-coding-review/input-validation.md |
