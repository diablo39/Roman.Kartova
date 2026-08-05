# Quality characteristics in review (ISO/IEC 25010:2023) — Performance efficiency

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics: time behaviour, resource utilization, capacity. Review against budgets, not
vibes: the deterministic subset is QUA-070 (no N+1/unbounded queries), QUA-071 (complexity bound
on hot paths), QUA-072 (bounded caches/queues/buffers), QUA-073 (claims require measurements).
Anything subtler — latency regressions, allocation churn — needs a profile or benchmark before
it becomes a finding; name the measurement you want run rather than asserting an impression.
Budget setting, benchmark and profiling method, and load/soak checks:
`knowledge/quality/performance-capacity.md` (QUA-074 enforces declared budgets).
