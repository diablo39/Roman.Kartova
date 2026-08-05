# Integration and data: pattern selection by quality scenario — Event sourcing and CQRS

Section of `knowledge/architecture/integration-and-data.md`.


Event sourcing stores the event log as the source of truth and derives current state by replay;
snapshots bound replay cost. CQRS separates the write model from one or more read models,
updated asynchronously from the write side. They're independent patterns, combinable but not a
package deal.

| Pattern | Justified by scenarios like | Standing costs |
|---|---|---|
| Event sourcing | Complete audit trail as a legal/domain requirement; temporal queries ("state as of March 31"); rebuildable projections; event-native domains (ledgers, trading) | Event schema evolution forever (upcasters/versioned events); eventually-consistent reads of your own writes; projection rebuild operations; steep learning curve — the highest-commitment pattern here, and reversal is a migration |
| CQRS | Read and write shapes genuinely diverge (write = normalized aggregate, read = denormalized search/report view); read:write ratio demands independent scaling; different consistency/latency budgets per side | Two models to keep mapped; staleness between them surfaces in UX; more deployables |

Safe default: CRUD on a relational store plus outbox events covers most services; it delivers
integration events and an audit-by-log without event sourcing's commitment. Reach for CQRS when
read-model scenarios force it (often just a projected read table, not a separate service);
reach for event sourcing only when scenarios name the event log itself as a requirement — and
record that decision as an ADR with the three-framing treatment
(`knowledge/shared/three-framing-analysis.md`), because it's a one-way door.
