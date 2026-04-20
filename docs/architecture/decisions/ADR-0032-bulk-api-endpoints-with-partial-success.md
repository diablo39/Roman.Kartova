# ADR-0032: Bulk API Endpoints With Partial Success

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST), ADR-0031 (rate limiting), ADR-0054 (scan coverage)

## Context

Auto-import (Phase 2) and CLI-driven imports will create or update hundreds to thousands of entities at a time. Per-item round trips are inefficient and fight rate limits (ADR-0031).

## Decision

Provide batch CRUD endpoints for each entity type that accept arrays of items. Responses include a per-item status so partial success is possible. A maximum batch size is enforced (e.g., 500 items); larger imports must be chunked by the client.

## Rationale

- Dramatic efficiency gain for CLI and scanner imports.
- Partial success avoids the "one bad item rolls back thousands" anti-pattern.
- Max batch size bounds server memory and tenant impact.

## Alternatives Considered

- **Single-item only + client batching** — wasteful over HTTP; fights rate limits.
- **JSON Patch** — good for targeted edits, not bulk create.
- **Streaming ingestion** — appropriate for very large imports; can be added later.

## Consequences

**Positive:**
- CLI and scanner throughput improved significantly
- Partial-success model surfaces recoverable errors

**Negative / Trade-offs:**
- More complex response shape — clients must handle per-item errors
- Validation and transactional semantics per batch must be clearly documented (atomic vs best-effort)

**Neutral:**
- Rate limiting still applies; bulk calls consume tokens proportional to items

## References

- Phase 0: E-01.F-06.S-03
