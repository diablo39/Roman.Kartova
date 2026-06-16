# Definition of Done — rationale

The compact nine-gate checklist lives in [CLAUDE.md](../CLAUDE.md) ("Working agreements → Definition of Done") and is the operative version. This file holds the *why* behind each gate so the checklist itself stays cheap to load every turn.

An implementation is "complete" / "finished" / "ready to merge" only when ALL nine gates are green and can be cited by command + output.

1. **Full solution build with `TreatWarningsAsErrors=true` (0 warnings, 0 errors).**

2. **Per-task subagent reviews (spec-compliance + code-quality) executed — never skipped on grounds of "trivial".** Review is cheap and its purpose is to force the pause that catches rationalization.

3. **`/superpowers:requesting-code-review` invoked at slice boundary against the full branch diff with spec + plan as context.** Catches cross-task design issues the per-task loop can't see (e.g., interaction between a filter defined in Task N and wiring in Task M).

4. **Full test suite green: unit + architecture + integration (Testcontainers).**

5. **For any slice that wires HTTP / auth / DB / middleware / pipeline: at least one `docker compose up` + real HTTP happy-path + one negative-path, output captured and confirmed.** Unit + architecture tests alone are the wrong layer of evidence for these slices — they won't catch filter-vs-binding order, JWT issuer/audience, `SET LOCAL` semantics, or Dockerfile restore gaps.

6. **`/simplify` skill run against the branch diff.** Surfaces reuse, code-quality, and efficiency findings the spec-and-quality reviews don't target. Should-fix items from each of the three review lenses (reuse / quality / efficiency) addressed or explicitly skipped with a reason.

7. **Mutation feedback loop run on changed files: `/misc:mutation-sentinel` (find surviving mutants) → `/misc:test-generator` (strengthen tests until mutants are killed).** Mutation score must meet the repo target (≥80% per `stryker-config.json`). Document the score and any surviving mutants accepted as low-value.

8. **`/pr-review-toolkit:review-pr` skill.**

9. **`/deep-review` skill run against the branch diff with spec / plan / ADRs / tests as context.** Produces a fixed-schema report (Blocking / Should-fix / Nits / Missing tests / What looks good). Blocking and Should-fix items addressed before merge; nits triaged.

Until all nine are green, the honest status is **"implementation staged, verification pending"** — never "slice N complete". If a step cannot be run locally (e.g., Docker unavailable on this machine), say so explicitly and flag as *pending user verification*, never imply completion. A Stop hook at `.claude/hooks/dod-check.js` blocks turns that assert completion without citing verification evidence.
