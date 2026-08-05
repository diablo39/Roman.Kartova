# Reliability and resilience

How our code stays correct when its dependencies do not. ISO/IEC 25010:2023 defines reliability
through faultlessness, availability, fault tolerance, and recoverability; this file translates
the last three into concrete controls a service builds in — timeouts, bounded retries, breakers,
declared failure modes, graceful degradation — and the tests that prove each control works. The
per-diff error-handling cluster (QUA-030 – QUA-034) is walked in
`knowledge/quality/review-method/error-handling-review.md`; oracle entries QUA-041 and
QUA-080 – QUA-082 point here — QUA-041's method is the timeouts section below. The governing rule: every remote interaction has a stated time bound, a stated retry
policy, and a stated failure mode — written in code or config, and verified by a test that
simulates the dependency failing. Hope is not a failure mode.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Timeouts | `knowledge/quality/reliability-resilience/timeouts.md` |
| Retries | `knowledge/quality/reliability-resilience/retries.md` |
| Breakers | `knowledge/quality/reliability-resilience/breakers.md` |
| Failure modes | `knowledge/quality/reliability-resilience/failure-modes.md` |
| Degradation | `knowledge/quality/reliability-resilience/degradation.md` |
| Failure-mode testing | `knowledge/quality/reliability-resilience/failure-mode-testing.md` |
