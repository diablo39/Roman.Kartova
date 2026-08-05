# Integration and data: pattern selection by quality scenario — Sagas — cross-service workflows

Section of `knowledge/architecture/integration-and-data.md`.


A business transaction spanning services (order → payment → inventory → shipping) becomes a
saga: a sequence of local transactions, each followed by an event or command, with compensating
actions to semantically undo completed steps when a later one fails. Compensation is business
logic (refund, release reservation), not rollback — some steps aren't compensable (email sent)
and belong last.

| | Choreography | Orchestration |
|---|---|---|
| Mechanism | Each service reacts to the previous event; no coordinator | A saga orchestrator tells each participant what to do next |
| Coupling | No central point; services know only events | Participants stay dumb; orchestrator knows the flow |
| Visibility | Flow exists only implicitly — reconstructing "where is order 4711 stuck?" spans n services | Flow and state are explicit and queryable in one place |
| Failure handling | Every service handles compensation triggers itself | Timeouts, retries, compensation sequencing in one component |
| Fits | 2–4 steps, stable flow, few conditionals | Longer flows, branching, human steps, operational visibility scenarios |

Safe default: choreography for short reactive chains, orchestration as soon as anyone asks "how
do I see where it's stuck?" — that question is a quality scenario (analysability) and it
decides. Sagas trade isolation for availability: intermediate states are visible (a placed
order whose payment later fails), so name those states in the domain model (`PENDING_PAYMENT`)
instead of pretending atomicity.
