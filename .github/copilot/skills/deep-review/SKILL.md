---
name: deep-review
description: Deep PR review against spec/plan/ADRs/tests. Produces a fixed-schema report (Overview / Blocking / Should-fix / Nits / Missing tests / What looks good). Output is mergeable across multiple reviewers. Template at docs/superpowers/templates/pr-deep-review-prompt.md.
---

# deep-review (GitHub Copilot CLI)

Port of the Claude Code slash command at `.claude/commands/deep-review.md`.
Tool calls are tool-agnostic prose — Copilot CLI maps them to its own
shell / file / search / agent tools at runtime.

## User input

The user invokes this skill with optional arguments:

- empty → review the **current branch** vs. `master`
- `<branch-name>` → review that branch vs. `master`
- `PR#<number>` or `#<number>` or `<number>` → fetch PR via `gh pr view`,
  review the head branch vs. the base branch
- `<commit>..<commit>` → review that commit range

If the form is ambiguous, ask the user before proceeding.

## Workflow

### Step 1 — Resolve the target

Parse the user's arguments per the forms above. Use `gh pr view` for PR
forms and `git rev-parse` / `git log` for branch and range forms.

### Step 2 — Discover the slice context

For the target branch/PR, locate the following files in the repo:

- **Spec(s):** `docs/superpowers/specs/` — files dated within the branch's
  lifetime, or referenced from the plan. There may be multiple specs for
  one slice (primary design + amendments).
- **Plan(s):** `docs/superpowers/plans/` matching the spec date(s).
- **ADR index:** `docs/architecture/decisions/README.md` (always include).
- **Mutation report:** `mutation-report-surviving.md` if present at repo
  root or under the slice's working directory. Optional.
- **DoD reference:** `CLAUDE.md` §Definition of Done (always include).

If specs/plans cannot be located, ask the user for paths before running.
Do not invent file paths.

### Step 3 — Fill the template slots

Open `docs/superpowers/templates/pr-deep-review-prompt.md`. Substitute the
discovered paths into the slot table. Use absolute slot values, not
guesses.

### Step 4 — Run the review

Execute the **prompt body** (the fenced block under `## The prompt` in
the template) against the diff. Two execution modes:

1. **In-session (default):** run the prompt as the reviewer. Read the
   diff, the spec(s), the plan(s), the cited ADRs, and the mutation
   report. Produce output in the exact schema specified.

2. **Fan-out (if user asks for ensemble):** dispatch parallel sub-agents
   with the filled prompt — Copilot CLI's agent dispatch is the
   equivalent of Claude Code's `Agent` tool. Then run the **merge step**
   (last fenced block in the template) against the outputs. Surface only
   the merged result.

### Step 5 — Save the result

Write the review to
`docs/superpowers/reviews/YYYY-MM-DD-<branch-or-pr>-review.md`
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

## Anti-patterns

- Do not skip Step 2. Diff-only reviews produce shallow output — the
  cross-reference against spec/plan/ADR is what makes this template earn
  its keep.
- Do not invent ADRs or spec sections. If the diff touches an area with no
  governing ADR, say so — that is itself a finding (architectural decision
  not yet documented).
- Do not pad the "what looks good" section. 3–5 specific items, with
  file:line. Not generic praise.
- Do not produce findings without `file:line` evidence — drop them.

## Tool mapping (Claude Code → Copilot CLI)

This skill was ported from a Claude Code slash command. Equivalents:

| Claude Code | Copilot CLI |
|-------------|-------------|
| `Bash` tool | shell tool |
| `Read` / `Write` / `Edit` | file tools |
| `Grep` / `Glob` | search tools |
| `Agent` (subagent dispatch) | Copilot CLI agent dispatch |
| `Skill` tool | `skill` tool |

If Copilot CLI's autodiscovery does not pick up this skill, verify the
plugin path matches your Copilot CLI configuration — see
`references/copilot-tools.md` from the `using-superpowers` skill for the
canonical tool/path mapping.
