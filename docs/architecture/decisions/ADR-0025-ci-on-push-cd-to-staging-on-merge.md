# ADR-0025: CI on Every Push; CD to Staging on Merge to Main

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** ADR-0022 (K8s)

## Context

A solo developer must ship frequently without breaking things. Trunk-based development with strong CI and automated staging deploys minimizes overhead and keeps feedback fast.

## Decision

- **CI** runs on every push and PR: build, unit tests, integration tests, lint, security scan, container image build.
- **CD to staging** runs automatically on merge to `main`.
- **CD to production** is triggered manually (tagged release) until auto-promotion rules are confidence-tested.

## Rationale

- Trunk-based avoids long-lived branches that a solo dev cannot review well.
- Auto-staging catches integration issues early without human gatekeeping.
- Manual production step preserves a safety rail during pre-revenue MVP.

## Alternatives Considered

- **GitFlow** — branching overhead not worth it for solo dev.
- **Manual staging promotion** — slows the feedback loop.
- **Ephemeral PR environments** — nice-to-have; can be added later.
- **Blue/green or canary from day one** — over-engineered until traffic justifies it.

## Consequences

**Positive:**
- Fast feedback, small diffs, low cycle time
- Production deploys remain deliberate

**Negative / Trade-offs:**
- Staging quality depends on test quality — test suite investment is non-optional
- Manual production step is a bottleneck as team grows (future concern)

**Neutral:**
- CI tooling choice (GitHub Actions / Azure Pipelines) will be decided alongside the Git provider decision

## References

- Phase 0: E-01.F-02.S-01, E-01.F-02.S-02
