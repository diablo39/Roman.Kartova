# Integration and data: pattern selection by quality scenario

This file is about choosing integration and data patterns — sync vs async, outbox, saga, event
sourcing, CQRS, consistency models, data ownership — and what each choice buys and costs.
Broker mechanics (streams, consumers, acks) live in the broker's own documentation;
message-contract design lives in `api-design.md`. Every pattern here should enter a design
through a quality scenario that demands it (`isaqb-concepts.md`), because each one adds moving
parts that must be operated and debugged.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Sync vs async selection | `knowledge/architecture/integration-and-data/sync-vs-async-selection.md` |
| The dual-write problem and the outbox | `knowledge/architecture/integration-and-data/the-dual-write-problem-and-the-outbox.md` |
| Sagas — cross-service workflows | `knowledge/architecture/integration-and-data/sagas--cross-service-workflows.md` |
| Event sourcing and CQRS | `knowledge/architecture/integration-and-data/event-sourcing-and-cqrs.md` |
| Consistency models | `knowledge/architecture/integration-and-data/consistency-models.md` |
| Data ownership | `knowledge/architecture/integration-and-data/data-ownership.md` |
| Choosing under pressure | `knowledge/architecture/integration-and-data/choosing-under-pressure.md` |
