# Integration and data: pattern selection by quality scenario — Data ownership

Section of `knowledge/architecture/integration-and-data.md`.


- One writer: each service owns its data store; no other service reads or writes those tables.
  The schema stays evolvable because the contract surface is the API/events, not the tables.
  Shared-database integration is the legacy exception — acceptable only where it pre-exists,
  with an expand-contract path out (`migration-patterns.md`).
- Cross-service reads, in order of preference: call the owning API (fine until fan-out/latency
  scenarios object); maintain a local projection from the owner's events (adds staleness,
  removes runtime coupling); CDC into a dedicated reporting/analytics store when queries span
  many owners (keeps OLTP stores clean; the warehouse/lake is a read model, never written back).
- Reference data (currencies, country codes): replicate read-only copies via events or periodic
  sync; don't create a chatty lookup service for slow-moving data.
- Published data is a contract: an event schema consumed by another team changes under the
  compatibility rules of `api-design.md`, with an owner on the hook — same discipline whether
  the transport is a broker, CDC, or a dataset.
