# ADR-0039: React SPA with TypeScript

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0027 (.NET API), ADR-0040 (dependency graph), ADR-0088 (shadcn/ui component library stack)

## Context

Kartova's web UI is rich, interactive, and authenticated — catalog navigation, dependency graphs, scorecards, status pages, admin surfaces (PRD §8). The frontend stack must match solo-dev familiarity, have a strong ecosystem, and integrate cleanly with the .NET backend via JWT (ADR-0007).

## Decision

Build the web UI as a React single-page application with TypeScript strict mode. Standard tooling: Vite for dev/build, React Router for routing, a well-maintained state/data layer (e.g., TanStack Query for server state), and a component library aligned with the design system.

## Rationale

- Broadest frontend ecosystem — components, tooling, hiring pool.
- TypeScript strict mode catches a large class of bugs at compile time.
- SPA + JWT fits the REST API model (ADR-0029) cleanly.

## Alternatives Considered

- **Next.js (SSR/RSC)** — excellent for SEO/marketing; overkill for an authenticated app and adds a Node server.
- **Remix** — similar trade-offs to Next.
- **Vue / Nuxt** — smaller ecosystem for enterprise components.
- **SvelteKit** — beautiful DX; smaller talent pool.
- **Blazor** — stack-consistent with .NET but ecosystem and third-party component depth is weaker.
- **HTMX + server templates** — attractive for small apps; interactive graph UI (ADR-0040) would fight the model.

## Consequences

**Positive:**
- Huge library ecosystem
- Fast dev loop with Vite
- Great DX

**Negative / Trade-offs:**
- SPA bundle size management needed
- SEO for public status page may need a separate static/SSR rendering path (acceptable trade-off)

**Neutral:**
- Public status pages may be pre-rendered or statically generated for performance

## References

- PRD §8
- Phase 0: E-01.F-01.S-02
