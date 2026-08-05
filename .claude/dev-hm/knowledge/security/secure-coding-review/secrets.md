# Secure code review by vulnerability class — Secrets

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-020 – SEC-023. Scan the whole diff — source, config, IaC, CI files, tests,
fixtures — for credential-shaped literals: high-entropy strings, `-----BEGIN` blocks, connection
strings with passwords, provider-prefixed keys (`AKIA…`, `ghp_…`, `sk-…`). Real-looking test
secrets fail: reviewers cannot distinguish them from leaks, and scanners flag them forever; use
obvious placeholders. Secrets reach code from the environment or a secret manager, through the
project's config module when one exists (SEC-023). Verify no secret-bearing value flows into
logs, exception messages, URLs, or generated docs (SEC-021/022). When a secret was committed and
then removed, the removal does not un-leak it — report it for rotation with `file:line` and kind,
never echo the value (see `knowledge/shared/ground-rules.md`).
