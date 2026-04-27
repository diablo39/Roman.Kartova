---
description: "Deep PR review against spec/plan/ADRs/tests. Produces a fixed-schema report (Overview / Blocking / Should-fix / Nits / Missing tests / What looks good). Output is mergeable across multiple reviewers. Template at docs/superpowers/templates/pr-deep-review-prompt.md."
---

## User Input

```text
$ARGUMENTS
```

## Instructions

Run a deep PR review using the canonical recipe at
`docs/superpowers/templates/pr-deep-review-prompt.md`.

### Step 1 — Resolve the target

Parse `$ARGUMENTS`. Accepted forms:

- empty → review the **current branch** vs. `master`
- `<branch-name>` → review that branch vs. `master`
- `PR#<number>` or `#<number>` or just `<number>` → fetch PR via `gh pr view`,
  review the head branch vs. the base branch
- `<commit>..<commit>` → review that range

If the form is ambiguous, ask the user before proceeding.

### Step 2 — Discover the slice context

For the target branch/PR, locate:

- **Spec(s):** look in `docs/superpowers/specs/` for files dated within the
  branch's lifetime, or referenced from the plan. There may be multiple specs
  for one slice (a primary design + amendments like
  `2026-04-24-defer-wolverine-persistence-design.md`).
- **Plan(s):** look in `docs/superpowers/plans/` matching the spec date(s).
- **ADR index:** `docs/architecture/decisions/README.md` (always include).
- **Mutation report:** `mutation-report-surviving.md` if present in repo root
  or under the slice's working directory. Optional.
- **DoD reference:** `CLAUDE.md` §Definition of Done (always include).

If specs/plans cannot be located, ask the user for paths before running.
Do not invent file paths.

### Step 3 — Fill the template slots

Open `docs/superpowers/templates/pr-deep-review-prompt.md`. Substitute the
discovered paths into the slot table. Use absolute slot values, not
guesses.

### Step 4 — Run the review

Execute the **prompt body** (the fenced block under `## The prompt`) against
the diff. Two execution modes:

1. **In-session (default):** run the prompt yourself as the reviewer. Read
   the diff, the spec(s), the plan(s), the cited ADRs, and the mutation
   report. Produce output in the exact schema specified.

2. **Fan-out (if user asks for ensemble):** dispatch two parallel `Agent`
   calls with the filled prompt — typically one Explore agent (deep code
   read) and one general-purpose agent. Then run the **merge step**
   (last fenced block in the template) against both outputs. Surface only
   the merged result.

### Step 5 — Save the result

Write the review to `docs/superpowers/reviews/YYYY-MM-DD-<branch-or-pr>-review.md`
using today's date (absolute, per project convention) and the branch / PR
identifier in the filename. Create `docs/superpowers/reviews/` if it does
not exist.

### Output to user

After saving, print a one-paragraph summary:
- target reviewed (branch / PR)
- counts: blocking / should-fix / nits / missing-test / good
- path to the saved report
- the top 3 blocking-class titles (or "no blocking findings")

Do not paste the full report into chat — it is in the saved file. Brief
chat output, full detail on disk.

### Anti-patterns

- Do not skip Step 2. Diff-only reviews produce shallow output — the
  cross-reference against spec/plan/ADR is what makes this template earn
  its keep.
- Do not invent ADRs or spec sections. If the diff touches an area with no
  governing ADR, say so — that is itself a finding (architectural decision
  not yet documented).
- Do not pad the "what looks good" section. 3–5 specific items, with
  file:line. Not generic praise.
- Do not produce findings without `file:line` evidence — drop them.
