# ADR-0040: Scoped Embedded Graph Views + One Standalone Explorer

**Status:** Accepted · **amended 2026-08-07** (see *Amendment*)
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0039 (React SPA), ADR-0088 (React Flow chosen as the rendering library)

> **Filename note:** the slug still reads `two-view-…` because ADRs are keyed by number and 31 files cite `ADR-0040`. The number is the identity; the slug is not.

## Context

Dependency graphs serve two very different user tasks (PRD §4.1.3): (1) glance at an entity's immediate neighbors while on its page, and (2) explore the broader graph of services, APIs, and infrastructure. Cramming both into a single UI hurts both.

## Decision

Render the graph on two **kinds** of surface, not a fixed number of views:

1. **Scoped embedded views** — each cut to its page's task and budgeted for it. **The scope is a property of the view, not a constant of this ADR:** the dependency mini-graph is a 1-level neighbourhood on a component page; the System diagram is a System's members and the edges between them, with external neighbours behind a toggle.
2. **One standalone `/graph` explorer** — full-screen, zoom/pan, filter by kind/team, and `?focus=<kind>:<id>` to enter at any node.

All of them share the data model and the **node grammar** — `EntityGraphNode`: node type, selection semantics, the ⋯ menu. A new embedded view is allowed when a page has a graph task the existing ones do not serve, provided it declares its scope and introduces no node renderer of its own.

## Amendment 2026-08-07 — the original phrasing was too rigid

As first written this ADR baked the number **two** into its title, its decision and its consequences, and defined the embedded view as "a 1-level neighborhood". Both were tighter than the reasoning required.

FU-A (E-03.F-03.S-01 closeout) added a third embedded view — `SystemDiagram` on the System detail Members tab, **depth-configurable** (1, or 2 behind an *Include external dependencies* toggle) — and with it a **membership channel** the original two views had no need for: a labelled background band behind the System's contents, plus a per-node "outside this system" marking (`outsideBoundary`).

Nothing in the original rationale forbade any of that; only the arithmetic in the title broke. The membership channel is recorded here rather than in the slice spec because it applies to any future embedded view that needs to say "these nodes are inside the thing you are looking at, those are not" — and because the slice that introduced it learned the hard way that the containment **cannot** be inferred from a bounding box: with `rankdir: "LR"` a non-member routinely shares a dagre rank, and so an x-column, with a member. Per-node marking is the mechanism; geometry is decoration.

See `docs/superpowers/specs/2026-08-05-catalog-system-graph-nodes-design.md` §3.1 for the measurement that forced it.

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
- One renderer per scoped view, plus the explorer. The shared `EntityGraphNode` keeps them visually consistent, but **every new view raises this cost — and that is the intended brake.** Adding one is a decision, not a default.
- Visual consistency has to be maintained deliberately across all of them, not just between two

**Neutral:**
- Graph library choice (e.g., Cytoscape, ReactFlow) is an implementation detail

## References

- PRD §4.1.3
- Phase 1: Feature E-04.F-02
