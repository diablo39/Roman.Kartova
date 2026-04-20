# ADR-0040: Two-View Dependency Graph Navigation (Mini + Standalone)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0039 (React SPA)

## Context

Dependency graphs serve two very different user tasks (PRD §4.1.3): (1) glance at an entity's immediate neighbors while on its page, and (2) explore the broader graph of services, APIs, and infrastructure. Cramming both into a single UI hurts both.

## Decision

Provide two views:

1. **Embedded mini-graph** — 1-level neighborhood rendered on every entity page. Fast, read-only, visually compact.
2. **Standalone `/graph` explorer** — full-screen, zoom/pan, filter by type/tag/owner, focus parameter to jump directly to a given entity (e.g., `/graph?focus=<id>`).

Both views share the same data model; the embedded view is a constrained projection.

## Rationale

- Matches the two distinct tasks (quick context vs deep exploration).
- Keeps entity pages fast by avoiding a heavyweight graph on every route.
- Standalone view can evolve (saved views, path queries) without cluttering every page.

## Alternatives Considered

- **Single modal graph** — jarring UX; hard to navigate.
- **Always-standalone** — loses the quick-glance use case.
- **Inline-only** — cannot handle deep exploration.
- **Embedded Backstage Catalog Graph** — not source-compatible; different data model.

## Consequences

**Positive:**
- Strong UX for both tasks
- Performance budgeted per view

**Negative / Trade-offs:**
- Two renderers to maintain (though they can share a library)
- Must keep visual consistency between the two views

**Neutral:**
- Graph library choice (e.g., Cytoscape, ReactFlow) is an implementation detail

## References

- PRD §4.1.3
- Phase 1: Feature E-04.F-02
