# Reliability and resilience — Degradation

Section of `knowledge/quality/reliability-resilience.md`.


The control: graceful degradation is designed, not discovered during the incident. When an
optional capability is down, the feature it powers is absent or reduced in a stated way — search
returns unranked results, recommendations disappear, writes queue with a notice — and the rest
of the product keeps working. The degraded user experience is stated in the work package, so
product owns it and tests can assert it. Under overload, shed load early and cheaply: bounded
queues and concurrency limits refuse excess work with 429/503 and Retry-After while there is
still capacity to refuse politely, rather than slowing down for everyone until nothing works
(the availability half of this is `knowledge/security/resource-protection.md` territory; the
design half lives here). Keep liveness and readiness distinct: a degraded instance stays live
but may report not-ready so the platform routes around it. Feature flags double as kill
switches — a flag that turns an expensive optional path off is the cheapest degradation
mechanism there is (flag lifecycle: `knowledge/quality/release-readiness.md`).

Verification: a test asserts the stated degraded behavior, not merely survival — with the
dependency stub down, the response still succeeds and contains the reduced payload or degraded
marker. A shedding test fills the bounded queue and asserts the next request is refused quickly
with the designed status, within a latency bound.
