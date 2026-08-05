# Migration patterns: evolving systems without stopping them

Migration is architecture work under the extra constraint that the system keeps serving traffic
at every intermediate step. The organizing idea comes from the ADM (`togaf-adm.md`): a sequence
of transition architectures, each one shippable, stable, and rollback-capable — never a
big-bang cutover with a long dark period. Big-bang replacement is the legacy exception: only
when the system is small enough to re-verify entirely and an acceptable downtime window exists,
and even then with a rehearsed rollback. This file gives the working patterns and when each
applies; database-migration mechanics (lock-safe DDL, online backfill) live in
`knowledge/quality/data-migration-safety.md`.

## Choosing the pattern

| Situation | Pattern |
|---|---|
| Replace a legacy system or extract services from a monolith, incrementally | Strangler fig |
| Replace a component/library/subsystem inside one codebase while trunk stays releasable | Branch by abstraction |
| Change a contract or schema that others depend on (API, events, database) | Expand-contract |
| Move data to a new store or shape while both old and new are live | Dual-write/dual-read sequence + backfill + verification |
| New code must consume a legacy model without inheriting its semantics | Anti-corruption layer |
| Reduce release risk for any of the above | Feature flags, canary, shadow traffic |

These compose: a strangler migration typically contains expand-contract steps for each
interface it moves, an anti-corruption layer at the seam, and flags/canaries per slice.

## Strangler fig

Incrementally build the new system around the old until the old one can be switched off.

1. Put an interception point in front of the legacy system — reverse proxy, API gateway, or
   event interception — so traffic can be routed per capability. Without this seam there is no
   incremental migration, only big-bang.
2. Pick a first slice that is valuable but loosely coupled (a capability with few shared
   tables); implement it in the new system; route its traffic over. The first slice's job is to
   prove the seam and the delivery pipeline, not to be big.
3. Repeat slice by slice. Each slice ends with legacy code path retired, not merely bypassed —
   route + retire is one unit of work.
4. Decommission: the migration is done when the legacy system is off. Track "percent of
   traffic/capabilities served by new" as the migration's fitness function
   (`architecture-evaluation.md`).

The dominant failure mode is stalling: both systems run for years, every change made twice, the
seam itself becoming load-bearing legacy. Countermeasures go in the plan, not in hope — a
decommissioning milestone per slice, an owner for the seam, and a standing rule that new
features land only in the new system (otherwise the old one keeps growing while being
strangled). Data is the hard part: while both systems touch the same domain, one of them is the
system of record per entity type at any time; document which, and move record-of-truth per
slice using the data-migration sequence below.

## Branch by abstraction

Replace a component inside one codebase without a long-lived VCS branch (the legacy exception
that mostly shouldn't be taken — merge pain grows superlinearly with branch age).

1. Introduce an abstraction over the component to replace; route all call sites through it
   (this step is plain refactoring, shippable continuously).
2. Build the new implementation behind the abstraction, in trunk, dark — covered by the same
   test suite via the abstraction.
3. Switch call sites (or a runtime flag) to the new implementation, incrementally where the
   call-site population allows; keep the old one until confidence is earned.
4. Delete the old implementation and, when it no longer pays rent, the abstraction itself.

Trunk stays releasable at every commit; progress is visible in code review instead of diverging
in a branch. The abstraction is scaffolding — leaving it in place after the swap is accidental
complexity unless a real second implementation is expected.

## Expand-contract (parallel change)

For any breaking change to a contract someone else depends on — API field, event schema,
database column, function signature with many callers. Never edit in place; overlap old and new:

1. Expand: add the new form alongside the old (new column, new field, new endpoint, new event
   version). Producers write both where applicable; nothing consumes the new form yet. Verify
   the addition is compatible (`api-design.md` compatibility table; oasdiff/buf gates).
2. Migrate: move consumers to the new form one by one; backfill historical data into the new
   form. Instrument usage of the old form — migration progress must be measurable, not assumed.
3. Contract: when old-form usage is verifiably zero (telemetry, not memory), remove the old
   form — deprecation signaling per `api-design.md`.

The contract step is the one that gets skipped, leaving both forms alive indefinitely: put it
in the plan as its own task with an owner and a date, gated on the usage metric reading zero.
For database schema steps under load (locking behavior, batched backfill), follow
`knowledge/quality/data-migration-safety.md`.

## Data migration between stores

Moving data while both old and new stores serve traffic; the sequencing goal is that every step
is observable and reversible until the final cutover.

1. Dual-write (via outbox/CDC rather than app-level double writes where possible — the
   dual-write hazard in `integration-and-data.md` applies to migrations too), old store remains
   source of truth.
2. Backfill history into the new store in batches; throttle to protect production.
3. Verify continuously: row counts, checksums per partition, and shadow reads — serve from old,
   compare against new, log divergence. Divergence rate is the go/no-go metric for cutover.
4. Cut reads over (per cohort or percentage), old store still written and warm — instant
   rollback is "point reads back".
5. Make the new store source of truth; keep the old one read-only through an agreed observation
   window, then decommission it — an old store kept "just in case" indefinitely is drift.

## Anti-corruption layer

When new code integrates with a legacy system (during strangling, or permanently at an external
boundary), an explicit translation layer maps between the legacy model and the new domain model
— adapters and translators owned by the new side. Without it, legacy semantics (status-code
overloading, sentinel values, entangled entities) leak into and corrupt the new model, and the
eventual legacy decommissioning no longer simplifies anything. The ACL is also where legacy
data-quality surprises get quarantined and logged instead of propagating. Cost: one more layer
to maintain, and mapping logic that can hide business rules — keep it translation-only, and
delete it with the legacy system.

## Release-risk techniques

Orthogonal to the structural patterns; pick per rollout, and remove after:

| Technique | What it derisks | Notes |
|---|---|---|
| Feature flag | Decouples deploy from release; instant kill switch | Flags are debt with a removal date; a migration flag outliving its migration is drift |
| Canary release | Real traffic, bounded blast radius | Needs per-cohort routing and comparable metrics between cohorts |
| Blue-green | Whole-deployment cutover with fast rollback | Watch stateful components — two app versions, one database, so schema changes still need expand-contract |
| Shadow / dark launch | New path correctness and capacity under real load, zero user impact | Duplicate traffic must not double-execute side effects (payments, emails) — stub or sandbox them |

## Transition architectures in the plan

Each phase boundary in the phased implementation plan should be a transition architecture: the
system runs, serves full traffic, and could stay in that state indefinitely if the migration
paused (funding cut, priorities shift) without leaving a booby trap. Steps 1–2 of
expand-contract are such a state; "half the call sites moved, old form removed" is not. Write
per phase: what is live, what is dark, which store/system is source of record for what, and the
rollback move. Migration choices that are one-way doors — record-of-truth flips, contract
removals, decommissioning — get ADRs, and the consequential ones the three-framing treatment
(`knowledge/shared/three-framing-analysis.md`).

Sources: martinfowler.com (StranglerFigApplication, BranchByAbstraction, ParallelChange),
*Monolith to Microservices* (Newman), TOGAF transition-architecture concept (`togaf-adm.md`);
anchors in `knowledge/shared/versions.md`.
