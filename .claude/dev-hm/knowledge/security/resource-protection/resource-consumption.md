# Resource protection — Resource consumption

Section of `knowledge/security/resource-protection.md`.


Every resource a request can consume has a stated bound. The inventory to check a new endpoint
or consumer against:

| Resource | Bound | Where |
|---|---|---|
| Bytes accepted | Request body and upload size caps | this file, input size limits |
| Parse work | Depth, element, and expansion limits | this file, parser and query bounds |
| Result size | Pagination and result caps | `knowledge/security/api-surface.md` |
| Execution time | Deadlines on handlers and outbound calls | this file, timeouts |
| Concurrency and memory | Bounded pools, queues, and buffers | this file, bounded queues and concurrency |
| Request rate | Per-principal budgets | `knowledge/security/api-surface.md` |
| Background work | Job quotas per principal | `knowledge/security/api-surface.md`; fairness below |

An endpoint that needs an exception ("this import really is unbounded") declares it in the
handoff with the compensating control — a quota, a dedicated worker pool, an operator gate —
named. Unbounded by accident is the failure this file exists to prevent.
