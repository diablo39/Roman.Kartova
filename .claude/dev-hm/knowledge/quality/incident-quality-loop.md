# Incident quality loop

A production incident is the most expensive test result the system ever produces; the loop in
this file is what converts that cost into permanent checks instead of folklore. It consumes the
SLI/SLO machinery from `knowledge/quality/observability/sli-slo.md` and
feeds three places: the test suite (a regression test per incident), the monitoring (a
detection fix per late-detected incident), and the gate rule set itself (an oracle or checklist
proposal per escaped defect class). Report editions for the delivery metrics cited here are
anchored in `knowledge/shared/versions.md`.

## Postmortems

Blameless and mechanical: the postmortem names contributing causes in the system — a missing
guard, an unbounded queue, a gap in a gate — never a person as root cause. "Human error" is a
label for an absent control: if one mistyped command can drop a table, the finding is the
absent confirmation step, not the typist. Blame produces hidden incidents; hidden incidents
produce no loop.

The artifact is structured enough to be actionable: timeline (detected when, by what, mitigated
when, resolved when), user impact quantified against the SLO, contributing causes (plural —
single-root-cause narratives are usually the write-up stopping early), what went well/poorly in
response, and action items each with an owner and a due date. Action items without owners are
wishes; a follow-up review that checks completion is part of the loop, not optional ceremony.

Severity classes for incidents are the operational cousin of the review tiers in
`knowledge/shared/severity-tiers.md` — a SEV-1 outage and an S0 finding
describe the same class of harm at different moments; the postmortem's job is to move the
discovery moment leftward.

## Regression tests from incidents

QUA-005 applied at incident scale: every incident whose cause lives in code or configuration
yields a test that fails on the pre-fix state and passes on the fixed state, both observations
recorded. The incident's identifier travels in the test name or comment so the suite documents
its own history.

- Reproduce by shape, not by copy: rebuilding the triggering data synthetically — same
  structure, same pathological values, fake content — keeps the regression test shareable and
  lawful (`knowledge/quality/test-data-management/the-pii-boundary.md`).
- Test at the lowest level that reproduces the failure
  (`knowledge/quality/test-strategy.md`): most incidents reduce to a unit
  or integration case; only genuine emergent behavior (load, timing, partial failure) needs a
  resilience or load test, and those follow
  `knowledge/quality/reliability-resilience/failure-mode-testing.md`.
- Defects cluster (ISTQB P4): an incident marks a hot module. Spend an hour testing its
  neighbors — the conditions adjacent to the one that failed — while context is loaded; the
  second bug is usually cheaper than the first.

Two more artifacts per incident, same discipline:

- Detection gap: if humans noticed before the monitoring did, the incident also yields an SLI,
  alert, or log fix (`knowledge/quality/observability.md`) — the 3 a.m.
  test failed and gets its own regression.
- Mitigation gap: if rollback or a flag kill-switch was slow or unavailable, the finding lands
  in `knowledge/quality/release-readiness.md#rollback` territory.

## Gate feedback

Every escaped defect passed the self-check, the reviewer, the gates, and the pipeline — so ask
which layer should have caught it and why it did not
(`knowledge/shared/defense-in-depth.md`):

- A deterministic, observable-from-the-diff defect that escaped means an oracle or checklist
  gap: propose the entry (stable ID, decidable pass criterion) or the addendum row. This
  feedback is the primary mechanism by which the rule set grows — oracle entries are fossilized
  incidents.
- A judgment defect that escaped review sharpens the review method or a knowledge file's
  walkthrough, not the oracle (an entry needing judgment is not an entry).
- A defect only observable in production (emergent load behavior, third-party surprise) is the
  monitoring's job; the yield is detection, not a new gate.
- Recurrence is the signal that the loop is broken: the same defect class escaping twice means
  the first postmortem's action item did not land — escalate the pattern, not just the
  instance.

## Error-budget policy

The SLO's error budget (defined, measured, and alerted per
`knowledge/quality/observability/sli-slo.md`) is the pre-agreed contract
that decides the feature-vs-reliability argument before an incident makes it emotional:

- Budget healthy: normal delivery; reliability work competes on ordinary priority.
- Sustained burn (the multi-window burn-rate alerts firing): reliability items take
  priority by prior agreement; the postmortem backlog gets scheduled, not deferred.
- Budget exhausted: feature releases to the affected service pause in favor of reliability
  work until the budget recovers — a policy the team signed before it hurt, applied without
  renegotiation during the incident.

The quality gate's touchpoint is release-bound: go/no-go for a release consults budget state
(`knowledge/quality/release-readiness.md#go-no-go`) — shipping a risky
change into an exhausted budget needs the same explicit, recorded waiver discipline as any
other gate exception. Budgets are per-SLO and per-service; a global freeze is policy failure,
not policy.

## Delivery metrics

The DORA metrics are the system-level gauge of whether the loop works — current metric set and
report edition per `knowledge/shared/versions.md`: throughput (deployment
frequency, lead time for changes) and instability (change failure rate, failed-deployment
recovery time, rework rate — deployments that exist only to fix a prior deployment).

- Read them as one system: throughput without the instability pair rewards shipping breakage;
  instability alone rewards shipping nothing. Research consistently finds speed and stability
  correlate — the gates that keep changes small and verified are the same machinery that makes
  deploys frequent.
- The incident loop shows up directly in the instability trio: change failure rate counts the
  escapes, recovery time measures mitigation readiness (rollback, flags), rework rate counts
  the fix-forward churn that regression tests should be shrinking.
- They are team-and-system signals for trend and investment decisions, never individual
  performance measures — metrics that become targets get gamed (batching deploys to look
  stable, reclassifying incidents), which destroys the loop's instrumentation. Watch trends
  and distributions, not single snapshots.

## What the gate asks

- A diff labeled as an incident fix: regression test present with both observations recorded
  (QUA-005), synthetic reproduction data, and the postmortem or incident ID referenced.
- Postmortem action items that landed as diffs: does the change actually close the named gap
  (the test fails pre-fix; the alert fires on the replayed condition)?
- Recurring finding classes at the gate itself: propose the oracle entry rather than
  re-litigating per diff — the gate is part of the loop, not just a checkpoint.
