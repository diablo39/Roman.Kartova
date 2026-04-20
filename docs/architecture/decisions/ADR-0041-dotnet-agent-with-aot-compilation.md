# ADR-0041: .NET Agent With AOT Compilation

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Agent Architecture
**Related:** ADR-0027 (.NET API), ADR-0042 (agent comms, pending), ADR-0043 (agent deployment), ADR-0046 (.NET CLI)

## Context

The Kartova agent runs inside customer Kubernetes clusters to discover services and ship metrics back (PRD §4.6, Phase 6 Epic E-15). Customers will weigh its resource footprint, startup time, and cross-platform availability. A single language across API, CLI, and agent simplifies solo-dev maintenance (PRD §8).

## Decision

Build the agent in .NET using AOT (Ahead-of-Time) compilation. AOT produces a small, self-contained, native binary with fast startup and low memory footprint. Targets: linux-x64, linux-arm64, windows-x64.

## Rationale

- Stack consistency across backend (ADR-0027), CLI (ADR-0046), and agent.
- AOT mitigates the main customer concern about .NET agents: startup time and memory.
- Cross-platform builds from the same codebase.

## Alternatives Considered

- **Go** — traditional agent language; requires a second toolchain and divergent code.
- **Rust** — smaller and faster but costlier dev time for a solo dev.
- **OpenTelemetry Collector as base** — powerful but opinionated; harder to bend to our discovery model.
- **eBPF-based** — very different deployment model; defer.
- **Existing Prometheus node_exporter + sidecar** — doesn't cover the service-discovery half of the agent.

## Consequences

**Positive:**
- One language across the platform
- Small, fast native binary

**Negative / Trade-offs:**
- AOT constrains reflection and some libraries — need discipline in dependency choice
- Customer perception of .NET on Linux may need active messaging

**Neutral:**
- Container image built on a minimal distroless/alpine base (ADR-0043)

## References

- PRD §8, §11 (Resolved Decision #4)
- Phase 6: Epic E-15
