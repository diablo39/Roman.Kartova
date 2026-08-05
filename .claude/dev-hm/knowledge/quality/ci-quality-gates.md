# CI quality gates

The repository-level controls that make the oracle's checks enforced rather than advisory:
branch protection, merge queues, artifact promotion, and the discipline of treating gate
configuration as code. The content of the test and coverage gates is defined in
`knowledge/quality/test-strategy/ci-test-gates.md`, and the canonical
stage order in `knowledge/quality/review-method/gate-order.md` — this
file does not restate either; it covers the machinery around them. Platform feature state
(protection-rule mechanisms, merge-queue capabilities) drifts; current anchors live in
`knowledge/shared/versions.md`.

## Branch protection

The default branch accepts changes only through a reviewed pull request whose required checks
passed. The load-bearing details:

- Required checks are named explicitly in the protection configuration. A CI job not marked
  required is advisory — it can fail while the merge button stays green — so the list of
  required checks is the actual gate, whatever the pipeline runs. Map it deliberately: the
  stages backing QUA-001/002/010/025 are required; slow or exploratory lanes report without
  blocking.
- Protection follows the platform's current policy mechanism (on GitHub, rulesets — layerable
  at organization level and applied across repositories — succeed classic per-branch protection
  rules; state per `knowledge/shared/versions.md`). Org-level policy
  beats per-repo settings that quietly diverge.
- No force pushes, no branch deletion, no direct pushes to the default branch — for anyone.
  Bypass lists are the gate's waiver mechanism: empty by default, and every entry is a recorded
  decision with a reason, reviewed like the S1 waiver it is. An admin who routinely bypasses
  checks has converted the gate into decoration.
- Required reviews complement, not replace, required checks: the review gate carries the
  judgment findings; the checks carry the deterministic oracle subset. Neither alone is the
  bar (`knowledge/shared/defense-in-depth.md`).

## Merge queues

A green PR plus a green main does not make a green merge: two PRs that each pass against the
main they branched from can break the main that contains both (a rename in one, a new call
site in the other — no textual conflict, a semantic one). On a busy trunk this happens weekly;
the classic mitigations (require branches up to date, merge-then-pray) either serialize the
team or let main break.

A merge queue closes the gap: PRs enter a queue, the platform constructs the candidate state
(main plus the queued PRs ahead), runs the required checks against that constructed state, and
merges only what passed as it will actually land. Batching amortizes CI cost across queued PRs;
a failing candidate is evicted and the queue re-forms without it.

Operational consequences worth designing for:

- CI must trigger on the queue's synthetic event/ref (not only on PR heads) or the queue
  silently tests nothing.
- Queue throughput is bounded by the required-check runtime — the speed budget in
  `knowledge/quality/test-strategy/ci-test-gates.md` becomes a hard
  constraint, and slow suites belong in post-merge or scheduled lanes.
- Flaky tests are queue poison: one flake evicts innocent PRs and re-runs the batch. The
  quarantine discipline (QUA-015, flakiness rules in
  `knowledge/quality/test-strategy/flakiness-control.md`) stops being
  hygiene and becomes load-bearing infrastructure.
- Small repositories with low merge concurrency do not need a queue; requiring up-to-date
  branches is the simpler equivalent at that scale. Adopt the queue when merge rate makes
  "up to date" a treadmill.

## Artifact promotion

Build once, promote the same artifact: the binary/image/package that passed the gates is —
byte for byte — the one deployed to every subsequent environment, referenced by immutable
digest, not by a mutable tag or a rebuild.

- Rebuilding per environment invalidates the evidence: a staging-approved build followed by a
  production rebuild ships an artifact no gate ever saw (different dependency resolution,
  different toolchain day, different flags). The gate record (QUA-100) attaches to a digest;
  a rebuild orphans it.
- Environment differences travel as configuration injected at deploy time, never baked into
  per-environment builds (`knowledge/quality/release-readiness.md`).
- Promotion is a recorded decision per stage — dev → staging → production — each step gated on
  that environment's checks (integration suites, soak, the release checklist). The aggregated
  verdicts that authorize promotion are the go/no-go record
  (`knowledge/quality/release-readiness.md#go-no-go`).
- Integrity and provenance of the artifact chain — signing, attestation, tamper resistance —
  are the security side's territory
  (`knowledge/security/supply-chain.md`); the quality claim here is
  narrower: the artifact tested is the artifact shipped.

## Gates as code

Pipeline definitions, protection rules, required-check lists, and coverage thresholds are
versioned files in the repository (or in a config-as-code policy repo), not console state:

- Reviewed like code: a gate change is a diff with an author, a reviewer, and a reason.
  Console-clicked policy has none of the three and drifts silently; periodic drift checks
  (exported settings compared against the committed policy) catch what the console changed.
- A change that weakens a gate — deleting a required check, lowering a threshold, adding a
  bypass — ships as its own diff, never bundled inside the feature it would have flagged.
  This is the pipeline-level form of the rule that lint/type config is not loosened in the
  diff it would flag (`knowledge/quality/review-method/lint-and-type-gates.md`),
  and of the gate agent's own boundary: never weaken a check to let a change pass.
- CI-class diffs get gate review like any other class: the scope step classifies them
  (`knowledge/quality/review-method.md`), and the reviewer walks what
  the change does to the enforcement surface — which checks stopped being required, what runs
  less often, who gained bypass.

## What the gate asks on CI-class diffs

- Does the diff remove, un-require, or soften any check backing an oracle entry? That is a
  gate-weakening change: it needs its own justification, not a feature ride-along.
- New required checks: deterministic, fast enough for the queue budget, and owned (a check
  nobody can fix blocks everybody).
- Pipeline changes that alter what artifact reaches production: does build-once promotion
  still hold, and does the gate record still attach to the shipped digest?
- Bypass and permission changes: every new bypass entry recorded with a reason, or flagged.
