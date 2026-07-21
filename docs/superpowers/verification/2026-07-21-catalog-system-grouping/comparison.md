# dev-hm A/B (2×2) — results

**Baseline:** `44fcb02` · **Arm A:** dev-hm (`armA/…`) · **Arm B:** control (`armB/…`)

## Implementer comparison (diff A vs diff B, reviewer held constant)

| Metric | Arm A (dev-hm) | Arm B (control, general-purpose) |
|--------|----------------|-----------------|
| Diff size (over baseline, incl tests) | 42 files, +2253/-10 | 42 files, +2245/-8 |
| Build warnings (first pass) | 0 | 0 |
| Full unit + real-seam integration | ✅ (257 unit / 32 new integ) | ✅ (256 unit / 34 new integ) |
| Production bugs surfaced by own tests | 0 | 0 (1 test-only `Location` assert fix) |
| Plan defects caught **independently** | **3** (Create sig, `System` BCL shadow, 422→400) | 0 (inherited the amended plan; 422→400 was **hinted in prompt** — contaminated, not independent) |
| Impl token cost (4 chunks) | ~787k | ~609k (~23% leaner) |
| Files touched delta | +`InvalidLifecycleTransitionException` (naming workaround) | cleaner (no workaround — plan pre-corrected) |
| Fairness context supplied | CLAUDE.md + spec + plan + amendment | same |

**Implementer read:** with a *detailed* plan, the two arms produced near-identical code (Δ 8 LOC, same file set, both green, both 0 warnings, both 0 prod bugs). The dev-hm arm's distinguishing value was **catching 3 plan defects during implementation** — but that signal is confounded: (a) it partly reflects my plan's imperfections, (b) Arm A ran *first* against the un-amended plan while Arm B got the corrected plan, so Arm B never had the chance to catch them, and (c) I hand-renamed Arm A during re-baseline. Control was ~23% cheaper in tokens. Net: on implementation quality alone, no meaningful dev-hm advantage on this slice; the defect-catching is real but not a clean apples-to-apples signal.

## Reviewer comparison (diff held constant, review stack varied) — the 2×2

| | dev-hm reviewer (`csharp-code-reviewer`) | default gate (`pr-review-toolkit:code-reviewer`) |
|---|---|---|
| **on diff A** | verdict PASS · 1 real (spec §7) + 1 delusion · **re-ran full build+unit+arch+integration** + per-ID oracle table | verdict Approve · 1 real (ADR/CHECKLIST process) + 1 delusion (self-ack "no bug") |
| **on diff B** | verdict pass-with-S2 · **4 real** (QUA-004 untested branch ★unique, CHECKLIST [x]-vs-pending, spec §7, steward coverage nit) + 0 delusion | verdict Approve · 2 real (spec §7, createdAt determinism) + 0 delusion |

- **Code defects (S0/S1/blocking) found by EITHER stack:** 0 — the implementation is a clean mirror of the well-tested `Api` template, no bugs to find.
- **★ Unique real caught only by dev-hm:** the **untested `createdByUserId==Guid.Empty` domain branch** on diff B (QUA-004 oracle) — default reviewer on the same diff missed it. This is the single clearest dev-hm advantage.
- **Unique real caught only by default:** none that dev-hm couldn't also reach — the ADR/CHECKLIST process issue was caught by default on diff A and by dev-hm on diff B (both stacks capable; coverage varied by run).
- **Convergent real (both stacks):** spec §7 `422→400` doc inaccuracy.
- **Delusion rate:** dev-hm 1/6 findings · gates 1/4 findings — comparable, both low.
- **Cost:** dev-hm ~436k tok (2 cells, and it re-ran test suites) · gates ~306k tok (~40% cheaper, static review only).

## Adjudication protocol
Findings judged real|delusion on merits in `gate-findings.yaml`, then tagged `produced_by`/`found_by`.

## Critique test (Phase 2a) — pre-implementation review of the ORIGINAL spec+plan

Both agents blind-reviewed the original (pre-amendment) spec+plan at a checkout with no answer key. Objective ground truth = the 3 defects that later surfaced during implementation.

| Known defect | dev-hm (`csharp-senior-architect`) | default (`general-purpose`) |
|---|---|---|
| D1 `Create` sig (missing TenantId / string createdByUserId) | ✗ missed | ✅ **blocking**, correct RLS reasoning |
| D2 `System` BCL-namespace collision | ✅ + **isolated repro build** (CS0118) | ✅ |
| D3 `422→400` disallowed pairs | ✅ + precedent test cites | ✗ missed |

**Novel real findings beyond the 3 (missed by the ENTIRE implement+4-review cycle):**
- default-only: `EntityKind.System` silently passes `/api-surface` + `/impact` guards (accidental capability vs deferred non-goal); `DependsOn` wildcard now accepts System edges any-direction (behavior widening).
- dev-hm-only: ADR-0111 §7 `contains` edge silently dropped (ADR-conformance); `Xmin`-409 dead error path (no edit endpoint).
- both: `SystemResponse` shape diverges from `ApiResponse` (drops `TenantId`/`CreatedByUserId`, invents `CreatedByDisplayName`); `FirstOrDefault` vs sibling `SingleOrDefault`.

**Read:** each agent caught 2/3 known defects (different ones; union = all 3) **plus** distinct real missed-impacts. dev-hm ≈ default here, complementary lenses (dev-hm: empirical rigor + ADR/convention discipline; default: behavior-widening breadth). **The dominant finding is stage, not agent:** pre-implementation critique caught more real issues, cheaper, than the whole downstream implement→review loop — because it reasons about the design before code exists.

## Generative test (Phase 2b) — author a fresh spec+plan from a locked brief

Both agents authored a design+plan from the same requirements brief, referencing the real `Api` template at a blind `master` checkout. Scored vs the 3 defects my ORIGINAL main-thread plan contained.

| | dev-hm (`solution-architect`) | default (`general-purpose`) | my original |
|---|---|---|---|
| D1 `Create` sig (Guid + TenantId) | ✅ correct | ✅ correct | ✗ wrong |
| D2 `CatalogSystem` naming | ✅ | ✅ | ✗ (`System`) |
| D3 `400` disallowed pairs | ✅ | ✅ | ✗ (`422`) |
| `/api-surface`+`/impact` guard | ✅ **hardened** (added the fix) | ✅ flagged, deferred | ✗ missed |
| Plan structure | prose/phased (296L design) | 10 bite-sized TDD tasks | bite-sized |

**Read:** BOTH fresh authoring passes produced markedly better plans than my original — all 3 defects avoided, missed-impact caught. dev-hm marginally more thorough (proactive guard hardening); default marginally better plan-execution structure. Again ~even between agents.

## Consolidated pros / cons — dev-hm agents (n=1, a SIMPLE slice)

**Context caveat (load-bearing):** this slice is a clean mirror of the battle-tested `Api` template — low novelty, low defect density, no planted bugs. That under-samples where agents earn their keep (novel/buggy code). Treat everything below as suggestive, not conclusive.

### Pros
- **Systematic coverage discipline (reviewer):** the oracle-gated `csharp-code-reviewer` caught a real untested domain branch (QUA-004) that the default reviewer on the same diff missed — the one clean per-cell win.
- **Verification rigor:** dev-hm reviewers re-ran the full build+suite themselves and produced per-ID SEC/QUA oracle tables; the dev-hm critic did an *isolated repro build* to prove the naming collision (CS0118) rather than asserting it.
- **Design/ADR-conformance lens:** the dev-hm critic uniquely caught the ADR-0111 `contains`-edge silent drop and a dead `Xmin`-409 error path; the dev-hm author proactively *hardened* the api-surface guard.
- **git-read-only by design:** implementers never committed — orchestrator stayed in control (matched the experiment's needs cleanly).

### Cons
- **No implementer advantage on a template slice:** dev-hm vs default implementations were near-identical (±8 LOC, both green, both 0 bugs); dev-hm cost ~20-40% more tokens across the board.
- **No decisive reviewer advantage:** on 3 of 4 review/critique cells the default agent matched or complemented dev-hm; each caught real things the other missed (complementary, not dominant).
- **Cost:** dev-hm dispatches ran consistently 20-45% more tokens (extra oracle reads + self-verification runs).
- **The real lever was STAGE, not agent:** a dedicated pre-implementation critique / fresh-authoring pass — by *either* agent — caught more real defects, cheaper, than the entire implement→review loop. My original plan's 3 defects came from inline authoring without a deep codebase-grounded pass, not from "no dev-hm."

### Verdict
On this (simple) slice, dev-hm agents are a **worthwhile supplementary lens** — their oracle/ADR discipline caught a few real things default missed — but they showed **no decisive, cost-justified advantage** over the default agents, and **zero** advantage as implementers. The highest-ROI change this experiment surfaced is process, not agent: **add a dedicated codebase-grounded critique of the spec+plan before implementation** (it caught 2/3 known defects + novel missed-impacts from a doc read). Re-run on a **defect-bearing / novel** slice before any firm keep/drop decision — this slice can't distinguish "good agent" from "easy task."

On this slice, **dev-hm reviewers added marginal but real value over the default gate — one systematic catch (an untested domain branch via the QUA-004 oracle) the default reviewer missed — plus more verification rigor (re-ran the full suite; per-ID oracle table), at ~40% higher token cost.** Neither stack found a code defect, because the code is a faithful mirror of a battle-tested template with no injected bugs.

**Strongest caveats:**
1. **n=1, and no planted bugs.** This measured "review of already-good code," which under-samples exactly where reviewers earn their keep — catching real defects. The dev-hm oracle discipline (systematic per-ID branch/security coverage) is most likely to pay off on genuinely buggy or novel code, not a template clone. A fair verdict needs a slice with real defects.
2. The **implementer** comparison was largely null (near-identical diffs) and partly contaminated (hand-rename of Arm A, plan pre-correction for Arm B). The 3 implementer-caught plan defects were Arm-A-only *by construction*, not a clean signal.

**Recommendation:** dev-hm's oracle-gated reviewers are worth keeping as a **supplementary lens for its systematic coverage discipline** (it caught a real coverage gap the default missed), but on low-defect slices the marginal value is thin and the cost higher. Do NOT drop the existing gates for it (no-folding). Re-run this comparison on a defect-bearing slice before drawing a firm conclusion.
