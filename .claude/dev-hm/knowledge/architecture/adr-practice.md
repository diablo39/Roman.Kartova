# ADR practice: MADR format, lifecycle, judgment

An Architecture Decision Record captures one architecturally significant decision with its
context, the options weighed, and the consequences accepted. ADRs exist because the rationale
evaporates: six months later the code shows what was decided but not why, and undocumented
decisions get relitigated or accidentally reversed. This plugin uses MADR (Markdown Any Decision
Records, adr.github.io/madr) — current version per `knowledge/shared/versions.md`.

## When to write one

Write an ADR when a decision meets any of these:

- Real alternatives were considered and a different team could reasonably have chosen otherwise.
- Reversal would be expensive (data model, protocol, framework, service boundaries).
- The decision constrains other work or other teams (shared conventions, API contracts).
- Someone will predictably ask "why is it like this?" later — including a surprising local
  choice that looks wrong without context.

Skip the ADR when the decision is forced (only one option survives the constraints — one line in
arc42 §2 suffices), trivially reversible, or pure implementation detail inside one module.
A steady project produces a handful of ADRs per quarter; dozens per month means the bar is too
low, zero per year means decisions are going unrecorded.

## Storage and naming

- Directory: use the repo's existing convention; otherwise `docs/decisions/` (MADR default).
- Filename: `NNNN-short-title-with-dashes.md`, zero-padded, monotonically increasing —
  `0007-use-nats-jetstream-for-event-bus.md`. Numbers are never reused.
- Link ADRs from the architecture document (arc42 §9) rather than restating them.

## MADR template

Full variant, annotated (the `---` metadata block is optional but recommended):

```markdown
---
status: accepted            # proposed | rejected | accepted | deprecated | superseded by ADR-NNNN
date: 2026-07-13            # when the decision was last updated
decision-makers: [hm, jd]
consulted: [platform-team]  # two-way communication (RACI "C")
informed: [qa-guild]        # one-way communication (RACI "I")
---

# Use NATS JetStream for the event bus

## Context and Problem Statement

Two to three sentences: the forces at play and the question to answer, ideally phrased as a
question. Reference the quality scenarios the decision affects.

## Decision Drivers

* Driver 1 — e.g. "at-least-once delivery required (reliability scenario R2)"
* Driver 2 — e.g. "team already operates NATS for RPC"

## Considered Options

* NATS JetStream
* Apache Kafka
* PostgreSQL-backed outbox with polling

## Decision Outcome

Chosen option: "NATS JetStream", because it satisfies R2 with the operational footprint the
team already carries; Kafka's stronger ordering guarantees aren't required by any scenario.

### Consequences

* Good, because no new infrastructure component to operate
* Good, because delivery semantics match scenario R2
* Bad, because long-term retention/replay beyond stream limits needs extra design
* Neutral, because consumers must be idempotent either way

### Confirmation

How compliance will be checked: e.g. "integration tests assert redelivery on consumer crash;
review checks that no service consumes events without an ack policy."

## Pros and Cons of the Options

### NATS JetStream
* Good, because ...
* Bad, because ...

(one subsection per option)

## More Information

Links: evaluation notes, spike results, related ADRs, superseded ADRs.
```

Minimal variant when the decision is simple: title, Context and Problem Statement, Considered
Options, Decision Outcome. Prefer starting minimal and expanding over writing boilerplate; a
filled-in template with empty ceremony sections reads as noise. (MADR 4.0 renamed "Deciders" to
"decision-makers" and "Validation" to "Confirmation", now under Decision Outcome; 4.x ships
annotated and bare variants of both full and minimal templates.)

Quality checks for the Consequences section: at least one "Bad, because" entry — a decision
with no downside wasn't analyzed (see the trade-off method in `isaqb-concepts.md`) — and
consequences phrased as observable effects, not restated benefits.

## Lifecycle

```
proposed ──> accepted ──> deprecated
    │            │
    └> rejected  └> superseded by ADR-NNNN
```

- proposed — drafted, under review. Useful for decisions the architect defers to the team.
- accepted — in force. The body of an accepted ADR is immutable: fix typos, but don't rewrite
  the decision. Changed circumstances mean a new ADR that supersedes this one.
- rejected — considered and declined; kept because "we looked at this and said no" is itself
  valuable context.
- deprecated — no longer relevant (the subsystem is gone), without a specific successor.
- superseded by ADR-NNNN — replaced; update the old record's status and cross-link both ways.

The supersede mechanism is what keeps history honest: readers can follow the chain and see how
understanding evolved, instead of finding a rewritten record that pretends the team always knew.

## Anti-patterns

| Anti-pattern | Why it hurts | Instead |
|---|---|---|
| ADR written after the code, reverse-engineered | Rationale is invented, options fictional | Write at decision time; a proposed ADR can precede the spike |
| Restating the ADR in wikis/design docs | Copies drift apart | Link to the ADR file |
| "We decided to use X" with no options | That's an announcement, not a record | Capture at least the runner-up and why it lost |
| Editing accepted ADRs as designs change | Destroys the historical record | Supersede |
| ADR as design document (10+ pages) | Nobody reads it; the decision drowns | One decision per ADR; move design detail to arc42 sections |
| Status forever "proposed" | Unclear what's actually in force | Time-box review; accept, reject, or drop |

Sources: adr.github.io/madr (version anchor in `knowledge/shared/versions.md`; templates at
github.com/adr/madr), adr.github.io for the broader ADR ecosystem.
