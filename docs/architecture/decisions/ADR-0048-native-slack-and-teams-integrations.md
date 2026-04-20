# ADR-0048: Native Slack & Microsoft Teams Integrations

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Notification Architecture
**Related:** ADR-0047 (notification engine), ADR-0057 (OAuth git provider connection)

## Context

Slack and Microsoft Teams are the dominant team-chat tools across Kartova's target segment. Critical notifications (incidents, breaking API changes, scorecard regressions) must arrive where teams actually read messages, not in yet another inbox (PRD §4.7.1). A "raw webhook" experience works but loses interactive actions (acknowledge, view details, assign) and rich formatting.

## Decision

Ship native integrations for both Slack and Microsoft Teams:

- **Slack**: OAuth-installed app with bot user; posts as Kartova; supports Block Kit for rich messages and interactive actions; per-channel routing configured in Kartova UI.
- **Teams**: Incoming webhook + bot (where available) with Adaptive Cards for rich formatting; per-team/channel routing.

Both channels are surfaced through the unified dispatch engine (ADR-0047). Raw webhook remains available as a fallback/advanced option.

## Rationale

- Dominant chat tools in target segment; must-have for day-to-day user adoption.
- Block Kit / Adaptive Cards enable actionable notifications (ack, assign, open in Kartova).
- OAuth install flow is self-serve and revocable by tenant admins.
- Investment amortizes across all notification types (incidents, scorecards, drift, agent events).

## Alternatives Considered

- **Webhook-only (user wires Slack/Teams themselves)** — works but misses interactivity, poor UX, no in-product install flow.
- **Add Discord/Mattermost/Google Chat at launch** — deferred; low demand in target segment; easy to add later via the same adapter pattern (ADR-0047).
- **Delegate to Courier/Knock** — same trade-offs as rejected in ADR-0047.

## Consequences

**Positive:**
- First-class UX in the tools teams already use.
- Interactive actions shorten time-to-response for incidents.
- Revocable OAuth model simplifies offboarding.

**Negative / Trade-offs:**
- Two distinct vendor APIs to track (Slack App Directory, Teams/Graph); rate limits and review processes differ.
- App store listing and review cycles add release overhead.

**Neutral:**
- Approval as a Slack/Teams Marketplace listing is optional and can be pursued post-launch.

## References

- PRD §4.7.1
- Phase 1: E-06a.F-03.S-01, E-06a.F-03.S-02
- Related ADRs: ADR-0047, ADR-0057
