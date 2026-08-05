# Performance and capacity — Profiling

Section of `knowledge/quality/performance-capacity.md`.


The control: profile before optimizing, because the guess about where time goes is usually
wrong, and an optimization without a profile is a maintainability cost with an unverified
benefit (`knowledge/quality/review-method.md#performance-review`). Sampling CPU profilers exist
for every stack we ship (async-profiler and JFR for the JVM, perf, py-spy, dotnet-trace, pprof,
platform tools for mobile); allocation profiling matters as much as CPU in garbage-collected
stacks, where allocation churn becomes tail latency. Read profiles as flame graphs; profile the
realistic workload — the empty loop optimizes beautifully and means nothing.

Continuous profiling in production closes the loop the lab can't: always-on, low-overhead
(eBPF-based) profilers collect fleet-wide profiles that can be diffed across releases, turning
"what got slower last deploy" into a lookup instead of an investigation. The ecosystem is
consolidating under OpenTelemetry, which is adding profiles as a fourth signal alongside traces,
metrics, and logs (maturity state per `knowledge/shared/versions.md`); treat the signal as
emerging and follow the collector's current support rather than assuming parity with the stable
signals.

Verification: a performance-motivated change carries the before-and-after measurement produced
by the same method, and ideally the profile that motivated it. Reviewers ask for the missing
measurement by name instead of debating intuitions.
