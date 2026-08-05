# Release readiness

Every work item has a definition of done enforced by the gates; a release is the sum of
items, and readiness for production is a property of the sum. This file defines that
property: what done means at each level, the go/no-go record, the rollback requirements, the
feature-flag lifecycle, and the checks that exist only at release scope. The quality oracle's
release entries point here. Severities and waiver authority are fixed in
`knowledge/shared/severity-tiers.md`; verdict grammar in
`knowledge/shared/defense-in-depth.md`.

## Definition of done

Item level — a work item is done when its evidence exists: build and tests green, coverage
floor met, both gate verdicts issued, every waiver recorded with its rationale. Done is a
property of the record, not a feeling; an item whose gate verdicts cannot be produced is not
done regardless of how finished the code looks.

Release level — a release is ready when every constituent item is done and the release-scoped
checklist at the end of this file passes. The second clause matters: a set of individually
done items can still be an unready release (an irreversible migration paired with an untested
rollback path, two items whose flags interact, budgets never checked for the combined
change).

What blocks a release versus rides the next one follows the severity tiers mechanically:
unresolved S0 or unwaived S1 findings on any constituent item block; S2 findings are fixed or
carry a recorded waiver; S3 advisories ride. Deferring a fix to the next release is
legitimate exactly once it is written into the go/no-go record — a deferral that exists only
in someone's head is an omission, not a decision.

## Go, no-go

The go/no-go decision is a record, not a meeting vibe. It exists so that "we decided this was
safe to ship" is checkable afterwards — by the gate before the release, and by whoever is
diagnosing it at 3 a.m. after. A release-bound change (version bump, release branch, deploy
configuration) carries the record; the record contains:

- Release identifier and scope: the work items, commits, or change set it aggregates.
- Aggregated gate verdicts: per item, the security and quality gate outcomes as summary
  counts, with fails and waivers itemized — the same grammar the gates emit, rolled up.
- Open risks: known issues riding this release, deferred fixes, waived findings that
  accumulate meaning at release scope.
- Release-scoped check results: the checklist below, each line pass, n/a, or waived.
- Rollback plan reference: where it is, and the evidence it was tested.
- The decision: go, no-go, or go with named conditions — decided by whoever the repository
  designates, with decider and date.

Aggregation is mechanical, which is what makes the record auditable: any constituent item
missing a gate verdict, or carrying an unwaived S1, makes the release a no-go by rule. The
record does not create the decision authority; it makes the decision inspectable.

Close the loop afterwards: a release that fails in production — rollback, hotfix, incident —
is a data point against the process, in DORA terms a hit to change failure rate. When the
same cause recurs, the release-scoped checklist gains a line; the go/no-go record is where
that learning attaches, because it shows what was checked when the failure shipped anyway.

## Rollback

Every release states how to get back, and the statement is verified, not vowed.

Present — the plan names, concretely:

- The previous deployable version, still deployable: artifact retained, environment
  compatible, deploy path exercised.
- Data compatibility in both directions. Migrations in the release are reversible or carry
  their recorded irreversibility justification
  (`knowledge/quality/data-migration-safety/reversibility.md`), and the previous application
  version tolerates data written by the new one — after hours of traffic, new-format rows
  exist, and a rollback that crashes on them is not a rollback. This is the same N-1
  compatibility expand-contract sequencing provides.
- The flag alternative: features that can be disabled without redeploying list their kill
  switch, because flipping a flag is a faster rollback than any deploy.
- Configuration, not just code: config and infrastructure changes shipped with the release
  have their own way back.

Tested — a rollback plan that has never been executed is a hypothesis. Acceptable evidence,
any one of: the deployment mechanism performs rollbacks routinely (previous-version redeploy
is a standard, recently exercised operation); a rehearsal in a staging environment, required
for releases containing migrations; or a release shape where rollback is traffic-shifting —
blue-green switchback or canary abort — exercised as part of the rollout itself. Progressive
delivery (canary, staged percentage rollout, blue-green) is the readiness-friendly default
where the platform supports it: it shrinks rollback from an emergency procedure to a routine
one and bounds the blast radius while the release earns trust.

State the expected time-to-rollback. A plan requiring an hour of manual steps is a plan to
have an hour-long incident; knowing that before the go/no-go decision is the point.

## Flags

Feature flags decouple deploy from release and provide the kill switch the rollback section
leans on — and every flag is a liability from creation to removal: a second code path,
untested combinations, and configuration that can drift. The lifecycle keeps the benefit and
retires the liability. Stages, vendor-neutral (providers name them slightly differently):
define → develop → ramp in production → cleanup → archived.

At creation, each flag declares — in code, in the flag system, or wherever the repository
documents flags:

- Default state: the value when the flag system is unreachable, which must be the safe path —
  the pre-change behavior (QUA-112 holds this deterministically, separate from the S3 hygiene
  items below). A flag whose failure mode enables the new behavior is a kill switch wired
  backwards.
- Owner: who answers for it and who removes it.
- Type, which sets the lifespan: release flags live weeks and are removed after full ramp
  plus a stabilization period; experiment flags live until the experiment reaches its
  decision; operational kill switches are long-lived and reviewed on a cadence; permission
  and entitlement flags are configuration and exempt from removal, but say so explicitly.
- Removal condition: a calendar date or an event — ramp at 100% and stable for the agreed
  period, the migration it guards completed, the legacy client version out of support. A
  flag without a removal condition is stale from birth.

Cleanup is a small release of its own: removing a flag deletes a code path, so it gets the
ordinary review and a one-line rollback story (re-add the flag, or revert). Stale-flag debt
is managed by automation where available — lifecycle stages in the flag system, CI checks
flagging expired flags, periodic housekeeping — rather than memory. A vendor-neutral
evaluation API (OpenFeature-style) keeps flag call sites portable across providers; that is a
repository-level decision, noted here only because it makes the lifecycle tooling swappable.

## Release-scoped checklist

The checks that exist only at release scope, run before the go/no-go decision and recorded in
it. Each line passes, is n/a with a reason, or is waived by the owning gate:

- Gate records aggregated across all constituent items; zero items missing verdicts, zero
  unwaived S1s (mechanics above).
- Migrations: expand-contract sequence stated, per-migration reversibility or justification
  present, backfill verification results recorded
  (`knowledge/quality/data-migration-safety.md`).
- Control-verification tests green in the release pipeline, none skipped or quarantined
  without a recorded reason (`knowledge/security/control-verification-tests.md`).
- Performance: declared budgets checked for paths the release touches; load or soak evidence
  where the release changes capacity-relevant behavior
  (`knowledge/quality/performance-capacity.md`).
- Rollback plan present, with its test or rehearsal evidence referenced (above).
- Flags in the release declared with default, owner, type, and removal condition; expired
  flags from previous releases not accumulating silently.
- Observability in place before traffic arrives: new functionality's metrics, alerts, and
  dashboards exist at release time, not after the first incident
  (`knowledge/quality/observability.md`).
- User-visible changes carry their changelog and documentation updates — the release-level
  view of QUA-050/051/053.

A short list, deliberately: every line is a failure class that individually-done work items
cannot catch, which is exactly what release readiness adds on top of done.
