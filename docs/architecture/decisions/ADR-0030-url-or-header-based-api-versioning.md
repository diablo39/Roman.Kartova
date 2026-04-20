# ADR-0030: URL- or Header-Based API Versioning

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST)

## Context

CLI tools (ADR-0046), agents (ADR-0041), and customer CI pipelines consume the API with long update cycles. Breaking API changes must be supported with a clear deprecation window (PRD §4.5.1).

## Decision

Use URL-based versioning (`/api/v1/...`) as the primary scheme, with an optional `Accept-Version` header for fine-grained negotiation if ever needed. Old versions are supported in parallel during a documented deprecation window (minimum 6 months). Only additive, non-breaking changes may be made within a version.

## Rationale

- URL versioning is visible in logs, easy to cache, easy to document, and easy for customers to pin.
- Header versioning is a fallback for edge cases (e.g., content negotiation).
- Explicit major versions align with REST conventions used by most large SaaS APIs.

## Alternatives Considered

- **Header only** — hard to debug from curl; invisible in logs.
- **Content negotiation** — too subtle; users forget the header.
- **Calendar versioning** — good for churn-heavy APIs; overkill here.
- **No versioning (additive-only)** — fragile once genuine breaking changes arise.

## Consequences

**Positive:**
- Predictable deprecation flow for customers
- Easy to operate multiple versions behind the same service

**Negative / Trade-offs:**
- Need to maintain more than one version in parallel during transitions
- Requires deprecation tooling (headers like `Deprecation`, `Sunset`)

**Neutral:**
- Internal endpoints (not part of the public API) may use a looser scheme

## References

- PRD §4.5.1
- Phase 0: E-01.F-06.S-01
