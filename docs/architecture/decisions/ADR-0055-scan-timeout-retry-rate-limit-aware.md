# ADR-0055: Scan Timeout 5 min/repo, Retry 3× with Backoff, Provider Rate-Limit Aware

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scan / Import Architecture
**Related:** ADR-0054 (deep scan), ADR-0056 (manual precedence), ADR-0057 (OAuth)

## Context

Scanning thousands of repositories across 1000+ tenants against third-party Git providers (GitHub, Azure DevOps) requires resilience patterns that balance throughput, completeness, and provider citizenship. Naive parallel scanning will hit provider rate limits; unbounded scan time will starve the queue; no retries means transient failures become permanent gaps.

## Decision

The scan subsystem operates under explicit resilience parameters:

- **Per-repo timeout: 5 minutes.** Repos exceeding this time mark the scan as `partial` and are queued for follow-up with an adjusted strategy (e.g., shallow clone, narrower extraction).
- **Retry: up to 3 attempts** with exponential backoff (1s, 5s, 30s) for transient failures (network errors, 5xx).
- **Rate-limit awareness**: scanners detect provider HTTP 429/403 with rate-limit headers (`X-RateLimit-*`) and automatically back off until the reset window. Per-tenant and per-provider token buckets prevent one tenant from exhausting shared quotas.
- **Partial results accepted**: if some extractors fail (e.g., Helm parser on a malformed chart) other extractor output is still persisted; failures are surfaced in the scan report.

## Rationale

- Bounded per-repo time keeps the global scan queue making progress — a single pathological repo cannot block the fleet.
- Three retries covers virtually all transient infrastructure failures without amplifying load on stressed providers.
- Rate-limit awareness is mandatory: GitHub's REST quota is 5000/hour per token, easily exhausted at scale.
- Partial-success semantics match the rest of the platform (ADR-0032 bulk endpoints) and preserve useful output from broken repos.

## Alternatives Considered

- **No timeout (streaming)** — unbounded scan time creates queue-starvation; operationally dangerous.
- **Shorter timeout (2 min)** — cuts out large monorepos that legitimately need longer; 5 min is the empirical sweet spot.
- **Queue-all, no retry** — transient provider hiccups turn into permanent catalog gaps.
- **Aggressive retry (infinite)** — amplifies rate-limit pressure; causes provider penalties.

## Consequences

**Positive:**
- Predictable scan-queue throughput even at 1000+ tenant scale.
- Good citizenship with Git providers — we won't get our OAuth app flagged.
- Partial results preserve useful data when extractors fail.

**Negative / Trade-offs:**
- Very large monorepos may persistently hit timeouts and require special handling (shallow/incremental scan mode).
- Rate-limit backoff can extend overall scan wall-clock time during busy periods.
- Retry+timeout interaction requires careful observability to diagnose "slow repo vs dead repo."

**Neutral:**
- Parameters are configurable per tenant/provider for future tuning.

## References

- PRD §7.2 (performance SLOs), §4.2.2
- Phase 2: Feature E-08.F-04 (feature-level)
- Related ADRs: ADR-0032, ADR-0054, ADR-0056, ADR-0057
