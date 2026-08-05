# Integration and data: pattern selection by quality scenario — Sync vs async selection

Section of `knowledge/architecture/integration-and-data.md`.


The real question is coupling in time: must the caller wait for the answer to proceed, and may
the caller fail when the callee is down?

| Situation | Default | Why |
|---|---|---|
| Caller needs the result to continue (query, validation, price calculation) | Synchronous request/response | The wait is intrinsic; async adds latency and machinery without removing it |
| State change must propagate to other services (order placed → notify billing, shipping) | Async event via broker | Consumers down ≠ producer blocked; new consumers attach without producer changes |
| Long-running or bursty work (report generation, media processing) | Async work queue | Absorbs bursts; retries without holding connections |
| Cross-service read for display | Sync call — until fan-out or latency scenarios say otherwise | Simplest thing that works; see composition options under data ownership |
| Third-party integration with strict delivery expectations | Async with persistent queue + retry at the boundary | Their availability is not yours; isolate it |

Two structural facts drive the async default for state propagation. Availability multiplies: a
request path chaining five synchronous 99.9% services delivers ~99.5% before anything goes
wrong; each sync hop couples your availability to theirs. And temporal coupling spreads outages:
a slow downstream backs up threads in every upstream (bulkheads and timeouts mitigate;
decoupling removes). The async costs are just as structural: eventual consistency surfaces in
UX, tracing needs correlation ids end-to-end, and every consumer must handle redelivery.

Mixed default for a state-changing request: validate and commit locally, respond, propagate
asynchronously. What must be true before responding stays sync; everything else becomes an
event.
