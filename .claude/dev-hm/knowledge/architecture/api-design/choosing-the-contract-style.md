# API design: contracts, versioning, compatibility, spec artifacts — Choosing the contract style

Section of `knowledge/architecture/api-design.md`.


Derive the choice from the quality scenarios (`isaqb-concepts.md`), not from fashion. Defaults
for new work, per rule 12 of the design principles (safe by default):

| Style | Default for | Chosen by scenarios about | Sacrifices |
|---|---|---|---|
| REST + JSON over HTTP | Public and cross-team APIs; anything a browser or unknown client calls | Interoperability, evolvability, cacheability, lowest consumer barrier | Verbosity; no streaming beyond SSE; free-form conventions need governance |
| gRPC (protobuf) | Internal service-to-service where both ends are owned and generated clients are acceptable | Low latency, typed contracts, bidirectional streaming, polyglot codegen | Browser access needs a proxy/transcoding layer; binary payloads hinder ad-hoc debugging |
| Async messages/events | State-change propagation across ownership boundaries; fan-out; long-running work | Temporal decoupling, availability under consumer outage, burst absorption | Eventual consistency; harder tracing; contract discipline moves to schemas + subjects (see `integration-and-data.md`) |
| GraphQL | Aggregation layer for many UI-shaped read patterns over several sources | Client-shaped queries, avoiding over/under-fetching across many view variants | Server complexity (N+1, depth/cost limits, caching); spec edition per `knowledge/shared/versions.md` |

These compose: a service commonly exposes REST publicly, gRPC internally, and events for state
propagation — one contract per audience, each versioned on its own clock.
