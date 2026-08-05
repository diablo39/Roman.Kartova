# PostgreSQL replication, HA, backup, and upgrades — Logical replication

Section of `knowledge/postgresql/replication-backup.md`.


Logical replication decodes WAL into row changes and applies them per table: publisher creates a
`PUBLICATION`, subscriber a `SUBSCRIPTION`. Unlike physical replication it is selective, works
across major versions and architectures, and the subscriber stays writable — which is what makes
it the upgrade and migration vehicle.

What it does not carry — plan around these:

- No DDL: schema changes must be applied to both sides (subscriber first for additive changes).
- No sequences: after a cutover, resync sequence values (`pg_dump --data-only` of sequences or
  `setval` from the publisher).
- `UPDATE`/`DELETE` need a `REPLICA IDENTITY` (default: primary key; `FULL` works without one but
  is expensive) — publishing a keyless table fails on the first update.
- Large objects are not replicated.

Capabilities by feature floor:

- Row filters and column lists on a publication (15) — publish a subset per table.
- Bidirectional safety via `origin = none` on the subscription (16): apply only locally-originated
  changes, preventing loops in two-way setups. Parallel apply of large streamed transactions (16).
- `pg_createsubscriber` (17): converts a physical standby into a logical subscriber in place —
  skips the initial table sync entirely for big fleets.
- Failover slots (17): `failover = true` on the subscription plus `sync_replication_slots = on`
  on the standby keeps logical slots synchronized to the physical standby, so logical consumers
  survive a failover of the publisher. Without this, a promotion strands every logical slot.
- Publishing stored generated columns, plus conflict logging and per-subscription conflict
  statistics in `pg_stat_subscription_stats` (18).

Operational notes: a logical slot holds xmin like any slot — monitor it the same way; initial
table sync copies data with COPY then streams (size the window); apply is single-threaded per
subscription except for the parallel-streaming case, so shard busy workloads across
subscriptions.
