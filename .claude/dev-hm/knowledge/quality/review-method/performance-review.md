# Review method — Performance review

Section of `knowledge/quality/review-method.md`.


Backs QUA-070 – QUA-073. Deterministic checks first: loops issuing per-iteration network/DB
calls where a batch API exists, and unpaginated queries against unbounded tables, fail QUA-070 —
the N+1 pattern is visible in the diff without profiling. Loops over unbounded input on request
or per-item paths carry a stated complexity bound (QUA-071); whether that bound is acceptable on
that path is judgment — report it as a `finding`, not a QUA-071 fail. Every cache, queue, or buffer gets a
size bound or eviction policy at introduction (QUA-072) — unbounded growth is a memory incident
with a delay timer. Everything subtler is measurement territory: performance claims in handoffs
carry a benchmark or profile, or are labeled expectations (QUA-073); reviewers request the
measurement rather than debating intuitions. Micro-optimizations that cost readability without a
measured hot path are findings against maintainability, not improvements.
