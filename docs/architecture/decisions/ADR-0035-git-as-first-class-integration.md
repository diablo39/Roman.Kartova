# ADR-0035: Git as First-Class Integration (Provider-Generic + GitHub + Azure DevOps)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0054 (deep scan), ADR-0057 (OAuth Git auth)

## Context

Auto-import and the deep repository scan (ADR-0054) require Git provider integration. Enterprise customers overwhelmingly use GitHub, Azure DevOps, GitLab, and Bitbucket (PRD §4.8.1, §4.8.2). MVP scope must be bounded.

## Decision

Ship Git integration in three layers:

1. **Provider-generic Git clone/scan** — works with any Git HTTPS URL + credentials (covers GitLab, Bitbucket, self-hosted, and long tail).
2. **First-class GitHub integration** — OAuth app (ADR-0057), webhooks, richer metadata (PR/issue context).
3. **First-class Azure DevOps integration** — OAuth, service hooks, metadata.

GitLab, Bitbucket, and GitHub Apps are deferred post-MVP.

## Rationale

- GitHub + Azure DevOps covers the majority of the target market.
- Provider-generic layer prevents excluding other providers completely.
- Narrows integration surface for solo-dev MVP while keeping extension possible.

## Alternatives Considered

- **Add GitLab, Bitbucket at MVP** — doubles integration surface.
- **Start with only one provider** — loses either the GitHub or the Azure DevOps segment.
- **SSH-key-based** — awkward UX, no webhook story.
- **GitHub App instead of OAuth** — more granular; re-evaluate in Phase 2+ (ADR-0057).

## Consequences

**Positive:**
- Covers most enterprise prospects from MVP
- Provider-generic fallback avoids hard exclusions

**Negative / Trade-offs:**
- Two OAuth integrations to maintain (tokens, webhook renewal, API changes)
- Feature parity between providers is work

**Neutral:**
- Future providers add incrementally rather than rework the foundation

## References

- PRD §4.8.1, §4.8.2
- Phase 2: Epic E-07
