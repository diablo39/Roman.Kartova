# Python review checklist

Severity-tiered catalogue for reviewing Python changes. Reviewers pair this with the
deterministic gate rules in `oracles/addenda/python.md` (SEC-PY-*, QUA-PY-*) and the core
`oracles/security-oracle.md` / `oracles/quality-oracle.md`. Severity tiers (S0 Block,
S1 Must-fix, S2 Should-fix, S3 Advisory) are defined in `knowledge/shared/severity-tiers.md`.
Report findings as `ID fail path:line — reason`; cite `file:line`, not pasted blocks.

The core oracle's S0 entries still apply to every Python change even though this checklist
does not restate them — hardcoded secrets (SEC-020), missing authentication/authorization
(SEC-010 – SEC-012), injection (SEC-001 – SEC-004), and disabled certificate validation
(SEC-031). A language-only review walk must still check them.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Correctness and Pythonic idioms | `knowledge/python/review-checklist/correctness-and-pythonic-idioms.md` |
| Typing | `knowledge/python/review-checklist/typing.md` |
| Exceptions | `knowledge/python/review-checklist/exceptions.md` |
| Performance and caching | `knowledge/python/review-checklist/performance-and-caching.md` |
| Concurrency and async | `knowledge/python/review-checklist/concurrency-and-async.md` |
| Security review hooks | `knowledge/python/review-checklist/security-review-hooks.md` |
| Structure and maintainability | `knowledge/python/review-checklist/structure-and-maintainability.md` |
