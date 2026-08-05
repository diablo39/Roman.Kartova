# Secure code review by vulnerability class

How to review a diff for each vulnerability class the security oracle covers. Section anchors
match the remediation pointers in `oracles/security-oracle.md`; each section says what to look for
in changed code, shows failing and passing patterns, and names the oracle entries it supports.
Severity and verdict mechanics live in `knowledge/shared/severity-tiers.md` and
`knowledge/shared/defense-in-depth.md`; standards versions: `knowledge/shared/versions.md`.

Reviewing for security is source-to-sink tracing: find where external input enters (sources),
find where it reaches an interpreter, filesystem, or trust decision (sinks), and verify a
control sits between them on every path — including error paths.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Injection | `knowledge/security/secure-coding-review/injection.md` |
| Output encoding and XSS | `knowledge/security/secure-coding-review/output-encoding-and-xss.md` |
| Access control | `knowledge/security/secure-coding-review/access-control.md` |
| Authentication and sessions | `knowledge/security/secure-coding-review/authentication-and-sessions.md` |
| Secrets | `knowledge/security/secure-coding-review/secrets.md` |
| Crypto and TLS | `knowledge/security/secure-coding-review/crypto-and-tls.md` |
| Input validation | `knowledge/security/secure-coding-review/input-validation.md` |
| Cross-origin and request forgery | `knowledge/security/secure-coding-review/cross-origin-and-request-forgery.md` |
| Browser protection headers | `knowledge/security/secure-coding-review/browser-protection-headers.md` |
| Files and paths | `knowledge/security/secure-coding-review/files-and-paths.md` |
| Deserialization and parsers | `knowledge/security/secure-coding-review/deserialization-and-parsers.md` |
| Exceptions and fail-closed behavior | `knowledge/security/secure-coding-review/exceptions-and-fail-closed-behavior.md` |
| Logging | `knowledge/security/secure-coding-review/logging.md` |
| SSRF | `knowledge/security/secure-coding-review/ssrf.md` |
| Memory safety | `knowledge/security/secure-coding-review/memory-safety.md` |
| Concurrency | `knowledge/security/secure-coding-review/concurrency.md` |
| Resource consumption | `knowledge/security/secure-coding-review/resource-consumption.md` |
