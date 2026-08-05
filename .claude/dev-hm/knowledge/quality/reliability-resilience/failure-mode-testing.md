# Reliability and resilience — Failure-mode testing

Section of `knowledge/quality/reliability-resilience.md`.


The discipline that keeps all of the above true over time: failure tests are ordinary tests in
the suite, run in CI on every change, not an annual ceremony. Fast tier: in-process fakes and
stub servers returning errors, timeouts, and garbage. Realistic tier: fault-injection proxies
(Toxiproxy toxics for latency, timeout, connection reset, bandwidth limits) between the code and
real engines in containers. Assert recovery, not just failure: after the fault clears, the
breaker closes, the queue drains, and normal behavior resumes — a system that fails gracefully
but never recovers has traded one incident for another. Chaos experiments in pre-production
(fault-injection platforms at the infrastructure level) verify the same properties end-to-end
and are worth running for critical systems, but they are an environment-level complement — never
a substitute for the per-dependency failure tests that live in the repo and run on every diff.
Every incident feeds back a failure test that would have caught it, the same way every bug fix
carries a regression test (`knowledge/quality/test-strategy.md`).
