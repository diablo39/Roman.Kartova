# Reliability and resilience — Failure modes

Section of `knowledge/quality/reliability-resilience.md`.


The control: every hard dependency has a written failure behavior, chosen at design time and
visible at the call site or in its client's config (QUA-081). Three honest options: fail fast —
propagate a typed error and let the request fail with a correct 5xx mapping (right for critical
dependencies whose failure is our failure); fallback — serve a cached, default, or reduced
result with its staleness or reduction marked (right for optional enrichments); queue-and-retry
— park the work in a durable buffer with bounded depth and replay it later (right for writes
that must eventually happen and can tolerate delay). Which option applies is a product decision
as much as a technical one; record it where the next reader will look.

Hunt hidden hard dependencies: a config service fetched at startup, a health check that blocks
on an optional system, a synchronous audit write — each turns an optional peer into a critical
one without anyone deciding that. Startup and readiness paths deserve the same failure-mode
declaration as request paths.

Verification: one test per dependency exercising the unavailable path — the stub refuses
connections or the container is stopped — asserting the declared behavior happens: the typed
error and status mapping for fail-fast, the fallback value with its degraded marker, or the
message present in the retry buffer. These live in the integration tier; Testcontainers plus a
fault-injection proxy (Toxiproxy has a Testcontainers module) simulate outage and latency
realistically without leaving CI.
