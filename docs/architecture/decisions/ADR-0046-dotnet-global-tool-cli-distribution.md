# ADR-0046: .NET Global Tool & Standalone Binary CLI Distribution

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** CLI & Distribution
**Related:** ADR-0027 (.NET backend), ADR-0041 (.NET agent), ADR-0007 (JWT auth)

## Context

Kartova ships a first-class CLI for catalog management, CI/CD integration, and policy enforcement (PRD §8, Phase 5). Users operate from mixed environments — developer workstations (macOS/Windows/Linux), CI runners, and container pipelines. The distribution mechanism must balance cross-platform reach with solo-developer maintenance cost and consistency with the rest of the stack.

## Decision

The CLI is distributed in two forms:

1. **`dotnet tool install -g kartova`** — for .NET-native users (developers with the SDK).
2. **Standalone self-contained binaries** (AOT-compiled where practical) for Linux x64/arm64, macOS x64/arm64, and Windows x64 — for CI runners and non-.NET developers.

Both are built from a single C# codebase. A shared authentication library handles OIDC device flow and service-account JWTs (see ADR-0007, ADR-0009).

## Rationale

- Stack consistency with the backend (ADR-0027) and agent (ADR-0041): one language, one test harness, one CI pipeline.
- `dotnet tool` is idiomatic for .NET-heavy enterprises (common in target segment).
- Standalone binaries remove the .NET SDK prerequisite for CI runners and non-.NET users.
- AOT keeps binary size and cold-start acceptable for CI usage.

## Alternatives Considered

- **Go binary** — fantastic distribution story, but breaks stack uniformity (new language, separate CI, no code sharing with backend/agent).
- **Rust binary** — similar to Go trade-off; additional solo-dev overhead.
- **npm-distributed Node CLI** — forces Node on every user; tree-shaking and cold start worse than AOT .NET.
- **Homebrew/winget only** — fine as additional channels but not sufficient for CI.
- **Container-based CLI** — awkward for local dev ergonomics; viable as a supplementary distribution.

## Consequences

**Positive:**
- One codebase, one language — lowest solo-dev maintenance.
- AOT binaries have no runtime prerequisite.
- Shared auth/SDK code with agent and future language clients.

**Negative / Trade-offs:**
- Build matrix must cover 5+ platform combinations.
- AOT compilation has quirks (no dynamic loading, reflection constraints).
- First-run on macOS requires notarization/signing to avoid Gatekeeper warnings.

**Neutral:**
- Winget/Homebrew/apt packages can be layered on top of the binary drops later.

## References

- PRD §8
- Phase 5: E-13.F-01.S-01
- Related ADRs: ADR-0027, ADR-0041, ADR-0007, ADR-0009
