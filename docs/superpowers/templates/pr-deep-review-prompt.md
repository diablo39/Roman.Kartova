# Deep PR Review Prompt

A tool-agnostic prompt template for slice-boundary PR reviews. Produces output
in a fixed schema so multiple reviews can be merged downstream.

Designed against the cc.md-style review of PR #1: catches architectural drift,
spec deviations, and missing tests — not just runtime bugs.

---

## Inputs to provide

Fill these in before pasting the prompt to the reviewer.

| Slot | Example |
|------|---------|
| `{BRANCH_OR_PR}` | `slice-2/auth-multitenancy` or `PR #1` |
| `{STATUS}` | `OPEN` (pre-merge gate) or `MERGED` (retrospective audit) |
| `{SPEC_PATHS}` | `docs/superpowers/specs/2026-04-22-slice-2-auth-multitenancy-design.md` |
| `{PLAN_PATHS}` | `docs/superpowers/plans/2026-04-22-slice-2-auth-multitenancy-plan.md` |
| `{ADR_INDEX}` | `docs/architecture/decisions/README.md` |
| `{TEST_TAXONOMY_ADR}` | `ADR-0083` (for missing-test analysis) |
| `{MUTATION_REPORT}` | `mutation-report-surviving.md` (optional) |
| `{DOD_REFERENCE}` | `CLAUDE.md §Definition of Done` |

---

## The prompt

```
You are reviewing {BRANCH_OR_PR}. Status: {STATUS}.

If MERGED: this is a retrospective audit — findings should land as follow-up
work, not block the existing merge.
If OPEN: this is a pre-merge gate — blocking-class findings must be resolved
before merge.

## Read against, not just at

Do not just read the diff. Read it **against** the following:

1. **Slice spec(s):** {SPEC_PATHS}
   For every spec section, ask: is this implemented? If yes, does the
   implementation match the design (interfaces, contracts, error semantics)?
   If the implementation diverged, is the divergence documented? Flag silent
   deviations.

2. **Slice plan: {PLAN_PATHS}
   For every task marked done, ask: are the acceptance criteria honored?
   Tasks marked complete with unmet acceptance criteria are blocking-class.

3. **ADR library: {ADR_INDEX}
   For every architectural choice in the diff (DI registration, data access,
   middleware order, transport contracts, error handling), find the relevant
   ADR. Flag deviations. Cite the ADR by number.

4. **Test taxonomy ({TEST_TAXONOMY_ADR}):**
   For every public surface added (HTTP endpoint, message handler, public
   API), confirm coverage at the appropriate tier (architecture, unit,
   integration, contract, E2E). Missing test tiers are findings.

5. **Definition of Done ({DOD_REFERENCE}):**
   For every gate, confirm it has been satisfied with citable evidence.
   Self-claims of completion without evidence are findings.

6. **(Optional) Mutation report: {MUTATION_REPORT}
   For every logic-class surviving mutant, identify the missing test. List
   actionable test cases by file:line. Equivalent / cosmetic mutants are not
   findings.

## Evidence rules

For every finding:
- Cite `path/to/file.ext:line` — the exact location.
- Cite the ADR / spec section / plan task being violated or honored.
- State the concrete impact: what breaks, what is masked, who notices.
- Propose a concrete fix: file, change, and (if applicable) the test that
  would have caught it.

Findings without file:line are not findings. Findings without a concrete fix
are observations, not review output.

## Output schema (use exactly these sections, in this order)

### Overview
2–3 factual sentences. What the slice ships. No opinions yet.

### Blocking-class issues
Issues that fail Definition of Done. For each:
- **Title.** One line.
- **Evidence:** `file:line` + cite.
- **Impact:** what breaks.
- **Fix:** concrete change.

If none, write "None." Do not pad.

### Should-fix issues
Issues that should land in a follow-up before the next slice. Same schema.

### Nits
Cosmetic / readability / minor. Same schema. Cap at 5 — if you have more,
your should-fix bar is too high.

### Missing tests
Acceptance criteria from the spec/plan with no corresponding test. Logic
survivors from the mutation report (if provided) with no test. For each:
- The acceptance criterion or mutant.
- The test that should exist (project, class, scenario, expected assertion).

### What looks good
3–5 design choices that are notably right. Mandatory section — review-only-
the-bad output is uncalibrated. Cite the file or pattern.

## Anti-patterns to avoid

- "This could be improved" without a concrete suggestion → not actionable.
- Putting style nits in the blocking section → severity inflation, you lose
  the reviewer's signal.
- Findings without file:line → not verifiable, drop them.
- Restating the diff → padding, drop it.
- Missing the "what's good" section → calibration failure.
- Inventing facts about ADRs you didn't read → if you don't have access to
  the ADR, say so, don't fabricate.
- Generic advice ("consider adding more tests") → name the test.
```

---

## Merge step (when running multiple reviewers)

When 2+ reviewers run this template, dedupe with this follow-up prompt:

```
You have N PR reviews of the same diff, each in the schema above.
Produce a single merged review with the same schema.

Rules:
- Dedupe by file:line. If two reviewers raise the same finding, keep the
  most-actionable phrasing and credit both reviewers in a (sources: A, B)
  tag.
- Resolve severity disagreements by taking the strictest tier. If reviewer
  A says blocking and B says should-fix for the same finding, it's blocking.
- Drop findings without file:line.
- "What looks good" — take the union, dedupe by topic.
- Final output is the prioritized to-do list. No commentary.
```

---

## Why this template

Empirical comparison of four PR reviews of the same merged slice (PR #1)
showed the deepest review outperformed the others because it:

1. **Read against spec/ADR/plan**, not just the diff. Caught architectural
   drift (e.g. a module using raw `AddDbContext` outside the tenant scope,
   middleware diverging from a spec'd endpoint-filter design) that
   diff-only reviewers missed.
2. **Cited spec sections** and ADR numbers for every architectural finding,
   making them verifiable.
3. **Tiered severity** (blocking / should-fix / nits) and named missing
   tests by spec section.
4. **Acknowledged what was good** — calibration that prevents reviewers
   from sliding into noise.

The shallow and deep outputs in that comparison came from the same model
with different prompts. The gap was prompting, not model capability. This
template captures the deep-prompt recipe.
