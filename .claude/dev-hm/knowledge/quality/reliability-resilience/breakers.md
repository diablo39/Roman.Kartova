# Reliability and resilience — Breakers

Section of `knowledge/quality/reliability-resilience.md`.


Circuit breakers and bulkheads. The control: a breaker per downstream dependency stops calling
a peer that is failing, which protects two budgets at once — the caller's deadline (fail in
microseconds instead of waiting out a timeout per call) and the dependency's recovery capacity
(a struggling service is not helped by full retry pressure). The cycle: closed while healthy;
open when the failure rate or slow-call rate over a sliding window crosses the threshold
(time-based windows behave better than count-based under bursty traffic); half-open after a
wait, admitting a few probe calls that decide between closing and reopening. The behavior when
open is the dependency's declared failure mode (next section) — a fallback or a fast typed
error, never a rethrow dressed up as resilience.

Bulkheads bound the blast radius the breaker can't: give each dependency its own bounded
concurrency — separate connection pools, bounded worker or semaphore limits — so one slow
dependency saturates its own compartment instead of every shared thread and connection in the
process. Sizing those bounds and the backpressure that keeps them honest is capacity work:
`knowledge/quality/performance-capacity.md#capacity`.

Join the repository's convention rather than inventing one (QUA-082): the same library, the
same configuration shape, the same metric names, so one dashboard shows every breaker. Breaker
state changes are logged and exported as metrics — an open breaker is an alertable event, and a
breaker nobody can see is a mystery outage with extra steps.

Verification: a test drives the full state cycle against a stub: enough failures open the
breaker; the next call fails fast and the stub receives no request (assert absence); after the
wait window a probe goes through; a successful probe closes it. Assert the state-change events
and metrics are emitted, because the alerting depends on them.
