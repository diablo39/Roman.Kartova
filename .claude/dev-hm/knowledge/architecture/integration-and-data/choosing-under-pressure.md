# Integration and data: pattern selection by quality scenario — Choosing under pressure

Section of `knowledge/architecture/integration-and-data.md`.


The patterns stack from cheap to committed: sync calls → events via outbox → saga → CQRS
read models → event sourcing. Move right only when a named quality scenario fails on the left,
and record the step as an ADR — each move right adds an operational competency the team keeps
paying for. When two adjacent options both plausibly satisfy the scenarios, take the left one.

Sources: microservices.io pattern catalog (Richardson), *Enterprise Integration Patterns*
(Hohpe/Woolf), *Designing Data-Intensive Applications* (Kleppmann), martinfowler.com (CQRS,
event sourcing); tool anchors (CDC tooling) in `knowledge/shared/versions.md`.
