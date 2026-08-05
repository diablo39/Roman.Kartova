# Integration and data: pattern selection by quality scenario — Consistency models

Section of `knowledge/architecture/integration-and-data.md`.


"Eventually consistent" is not one thing; name the guarantee per data flow and check it against
the scenario:

| Model | Guarantee | Typical home |
|---|---|---|
| Strong / linearizable | Every read sees the latest committed write | Single-node RDBMS; quorum systems; within one service boundary |
| Read-your-writes | A client sees its own writes | Session pinning, or read-from-primary after own write — the minimum users notice missing ("I saved it and it's gone") |
| Monotonic reads | A client never sees state go backwards | Sticky replica routing |
| Causal | Effects never visible before their causes (reply before the message it answers) | Ordered per-entity event streams; careful projection design |
| Eventual | Replicas converge, no timing bound | Cross-service projections, caches, search indexes |

Working rules: inside one service boundary, take the strong consistency the local database
gives you — don't import eventual consistency where nothing demands it. Across boundaries,
eventual is the honest default; the design work is bounding and surfacing staleness (show
"processing…" states rather than stale data as current; per-entity ordering via broker subject
design). Write the acceptable staleness into the quality scenario ("search reflects a new
product within 30 s") so it's testable instead of vibes.
