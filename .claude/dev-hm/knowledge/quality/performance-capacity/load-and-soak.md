# Performance and capacity — Load and soak

Section of `knowledge/quality/performance-capacity.md`.


The control: before releasing a new service or a significantly changed hot path, three checks
answer three different questions. Load: at expected peak traffic (times a headroom factor), does
the latency budget hold? Stress: pushing past peak, where is the knee — the throughput at which
latency degrades — so the capacity limit is a measured number, not a guess? Soak: at moderate
sustained load over hours, does anything leak — memory, connections, file handles, queue depth,
disk? Leaks and slow drifts are invisible to short tests by construction; the soak's pass
criterion is a flat resource trend over the run, asserted on the slope, not merely on surviving.

Practice: load scenarios are code, versioned in the repo, written for the team's tool (k6 with
thresholds as native CI pass/fail gates; Gatling and Locust are equally capable — pick one and
accumulate scenarios). A workable cadence: a lightweight load check on hot endpoints per merge
or nightly, the full load and stress suite before releases, soak on a schedule for long-running
services. Be honest about environments: a staging run tells you about relative regressions and
leak behavior; absolute numbers transfer to production only when the environment does. Label
results with where they ran.

Verification: for release-bound changes, the handoff records the run — tool, scenario,
environment, measured numbers against the budget (QUA-074,
`knowledge/quality/release-readiness.md` aggregates this at the release). Threshold breaches
fail the pipeline run, so a regression is a red gate, not a footnote.
