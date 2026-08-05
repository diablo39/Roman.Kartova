# Quality characteristics in review (ISO/IEC 25010:2023) — Reliability

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics (2023): faultlessness, availability, fault tolerance, recoverability. In
review this is the error-handling cluster: no swallowed errors (QUA-030), causes preserved
(QUA-031), error returns handled (QUA-032), fail-fast preconditions (QUA-033), timeouts on
external calls (QUA-041), and fail-closed on security paths (SEC-071). Ask "what happens when
the dependency is down, slow, or returns garbage?" for every new external interaction; the
answer belongs in code, not in hope. Retries need bounds and backoff; idempotency is a
precondition for safe retry, not an afterthought. Timeout, retry, breaker, degradation, and
failure-mode-testing method: `knowledge/quality/reliability-resilience.md` (QUA-080 – QUA-082);
recoverability of data moves: `knowledge/quality/data-migration-safety.md` (QUA-091 – QUA-092);
recoverability of releases — rollback plans and flag safe defaults:
`knowledge/quality/release-readiness.md` (QUA-101, QUA-112).
