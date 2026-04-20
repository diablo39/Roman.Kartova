# ADR-0029: REST as Primary API Style

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0030 (versioning), ADR-0032 (bulk endpoints), ADR-0034 (OpenAPI)

## Context

Kartova exposes APIs to multiple consumer types: web UI, CLI (ADR-0046), CI/CD integrations, agents, and third-party webhook senders/receivers (PRD §4.5.1). The API style must be approachable for all of them and usable with plain curl/Postman.

## Decision

Use REST as the primary API style: resource-oriented URLs, standard HTTP verbs, JSON payloads, consistent error envelope, pagination via cursors, and full CRUD coverage for all nine entity types (ADR-0064). OpenAPI specs are generated automatically (ADR-0034).

## Rationale

- Broadest tool support (curl, Postman, OpenAPI codegen, every HTTP client).
- Simplest mental model for CLI consumers.
- Existing ASP.NET Core idioms are REST-native.

## Alternatives Considered

- **GraphQL** — fits the graph of entity relationships but adds schema, caching, N+1, and tooling complexity; may be added later as a complementary read API.
- **gRPC** — efficient but requires codegen for every client; unsuitable as the public API.
- **tRPC** — Node-specific; irrelevant with a .NET backend.
- **OData** — overkill and less common in the target audience.

## Consequences

**Positive:**
- Low barrier for customers to integrate
- Standard tooling works out of the box

**Negative / Trade-offs:**
- Graph queries are inefficient over REST — may justify GraphQL later for dependency-graph UI (ADR-0040)
- Versioning discipline is mandatory (ADR-0030)

**Neutral:**
- Webhooks (ADR-0033, pending) still carry JSON payloads for parity

## References

- PRD §4.5.1
