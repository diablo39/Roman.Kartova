# ADR-0099: Distributed Locking and Leader-Elected Periodic Tasks via Postgres Advisory Locks

**Status:** Accepted
**Date:** 2026-05-27
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** ADR-0001 (PostgreSQL), ADR-0080 (Wolverine — durability deferred), ADR-0082 (modular monolith)

## Context

Several upcoming work streams need periodic background work running safely across multiple application instances: invitation expiry (slice 9), notification dispatch retry (E-06a), scheduled re-scans (E-08), scorecard recompute (E-10), data retention purge (E-01.F-05), agent aggregation (E-15). Wolverine has durable scheduled messages but its persistence is explicitly deferred per ADR-0080.

## Decision

Adopt Postgres session-level advisory locks (`pg_try_advisory_lock`) as the distributed-locking primitive. Provide three reusable building blocks: `IDistributedLock` abstraction in `Kartova.SharedKernel`, `PostgresAdvisoryLock` implementation in `Kartova.SharedKernel.Postgres`, and `LeaderElectedPeriodicService` base class in `Kartova.SharedKernel`. Periodic services declare a `LockName + Interval` and an `ExecuteLeaderWorkAsync` implementation; the base class handles timer + lock acquisition + scope creation + exception isolation.

## Consequences

### Positive

- Locks auto-release on connection drop, so there is no stale-lock recovery path to design, test, or operate.
- Multi-instance safe — only one replica holds a given named lock at a time, which is exactly the leader-election semantics every periodic task needs.
- No new infrastructure: Postgres is already a required dependency (ADR-0001), so this introduces zero new runtime components, ops surface, or Helm wiring.
- Reusable across every future periodic task — invitation expiry, notification dispatch retry, scheduled re-scans, scorecard recompute, retention purge, agent aggregation all share one primitive.
- Doesn't force the Wolverine-persistence decision; this primitive is orthogonal to ADR-0080's deferral and can coexist with future durable-messaging work.

### Negative / trade-offs

- Each tick opens a new connection per acquisition attempt, which is modest overhead at small scale but at very high tick rates could pressure the `BYPASSRLS` pool used for cross-tenant maintenance work. Acceptable for hourly / daily ticks; an alternative would be needed only if we end up with second-by-second leader work.

### Upgrade path

When Wolverine durability is enabled in a future slice, periodic work *can* migrate to Wolverine scheduled messages, but the existing primitives remain valid and can stay where they are — no forced migration.
