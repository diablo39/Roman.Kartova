# DoD Ledger Convention + Verification Consolidation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a per-slice DoD ledger (`verification/<date>-<topic>/dod.md`) that records citable status for every CLAUDE.md DoD gate, merge `reviews/` + `evidence/` into that one per-slice tree, and enforce ledger citation via the stop hook.

**Architecture:** Docs convention + one hook edit. A markdown template defines the fixed ledger schema; the `dod-check.js` stop hook gains a rule requiring completion claims to cite a `verification/<…>/dod.md` path; `CLAUDE.md` mandates the ledger; existing artifacts are `git mv`'d into the new tree and all live references repointed.

**Tech Stack:** Markdown, Node.js (the stop hook), Git, Bash (migration). No production C#/TS.

**Spec:** `docs/superpowers/specs/2026-06-26-dod-ledger-convention-design.md`
**Branch:** `feat/catalog-dependency-mini-graph` (current branch, per the human's decision — this rides along with PR #46).

## Global Constraints

- **Ledger location & name:** `docs/superpowers/verification/<date>-<topic>/dod.md`, `<date>-<topic>` identical to the slice's design/plan slug. The file is literally named `dod.md`.
- **Gate list is authoritative in `CLAUDE.md §Definition of Done`** — the ledger *records* the 9 gates (gate 6 conditional) + Manual/Playwright (ADR-0084) + Terminal re-verify + Pre-push CI mirror. Never invent or renumber gates here; copy the current list verbatim. Do **not** reuse the stale slice-9 9-bullet numbering.
- **Status vocabulary:** `✅ PASS` / `❌ FAIL` / `⏳ PENDING` / `N/A`. `FAIL` and `N/A` MUST carry a one-line reason.
- **Hook edit (`.claude/hooks/dod-check.js`) is plain Node**, no deps; it reads a JSON object on stdin with `transcript_path`, then inspects the last assistant message in that JSONL transcript.
- **Serena-guard:** the hook is `.js` → editable with the built-in `Edit` tool (the guard only hard-blocks existing `.cs`/`.ts`/`.tsx`). The one `.cs` touched in Task 5 (`KartovaApiFixtureBase.cs`) MUST go through Serena `replace_content`, and its line MUST be CRLF-normalized after edit (LF repo, Windows host flips it — see project memory).
- **Migration is one isolated commit**, separate from the template/hook/CLAUDE.md/backfill commits. Use `git mv` so history is preserved.
- **Windows shell:** PowerShell or `cmd //c` for tooling; the migration uses the Bash tool (POSIX). All `git` runs from repo root.

---

### Task 1: DoD ledger template

The fixed-schema empty ledger every slice copies.

**Files:**
- Create: `docs/superpowers/templates/dod-ledger-template.md`

**Interfaces:**
- Produces: the template file. Task 4 copies it to a real ledger; Task 3 (CLAUDE.md) and Task 2 (hook reason) reference its path.

- [ ] **Step 1: Write the template**

Create `docs/superpowers/templates/dod-ledger-template.md`:
```markdown
# DoD Ledger — <Slice / Topic>

**Slice:** `<date>-<topic>` · **Branch:** `<branch>` · **HEAD:** `<short-sha>`
**PR:** <#NN / url> · **Last updated:** <YYYY-MM-DD>
**Spec:** `docs/superpowers/specs/<date>-<topic>-design.md`
**Plan:** `docs/superpowers/plans/<date>-<topic>.md`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | ⏳ PENDING | — |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING
**Evidence:** <command + output excerpt, or CI run URL>
**At:** <commit / date>

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING
**Evidence:** <subagent ids / linked report files>
**At:** <commit / date>

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING
**Evidence:** <command + counts, or CI run URL. Note real-seam N/A with reason if frontend-only>
**At:** <commit / date>

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING
**Evidence:** <CI "Container images" check URL>
**At:** <commit / date>

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** <link to simplify.md / findings summary>
**At:** <commit / date>

### 6 — Mutation loop (conditional: Domain/Application changes only)
**Status:** ⏳ PENDING
**Evidence:** <score + survivors, or N/A reason (no Domain/Application change)>
**At:** <commit / date>

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING
**Evidence:** <link to requesting-code-review.md / findings>
**At:** <commit / date>

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** <link to review-pr.md / PR review>
**At:** <commit / date>

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** <link to deep-review.md>
**At:** <commit / date>

### Manual / Playwright verification (ADR-0084)
**Status:** ⏳ PENDING
**Evidence:** <screenshots folder / console-clean note, or N/A reason (no UI change)>
**At:** <commit / date>

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** <command + output / CI run URL>
**At:** <commit / date>

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⏳ PENDING
**Evidence:** <command + result, or CI run URL (the runner is the mirror's source of truth)>
**At:** <commit / date>
```

- [ ] **Step 2: Verify it renders / no broken structure**

Run (Bash, repo root):
```
test -f docs/superpowers/templates/dod-ledger-template.md && grep -c '⏳ PENDING' docs/superpowers/templates/dod-ledger-template.md
```
Expected: prints `25` (12 summary rows + 12 detail blocks + the 1 legend mention, all `⏳ PENDING`). If the count is lower, a row is missing — fix before commit.

- [ ] **Step 3: Commit**

```
git add docs/superpowers/templates/dod-ledger-template.md
git commit -m "docs(superpowers): DoD ledger template (fixed-schema, current gate list)"
```

---

### Task 2: Extend the `dod-check.js` stop hook to require ledger citation

The hook already blocks completion claims lacking evidence. Add: a completion claim MUST cite a `verification/<…>/dod.md` ledger path, else block.

**Files:**
- Modify: `.claude/hooks/dod-check.js`

**Interfaces:**
- Consumes: stdin JSON `{ transcript_path }`; the last assistant message text.
- Produces: `{"decision":"block","reason":…}` on stdout (+ exit 0) when a completion claim lacks a ledger citation; silent exit 0 otherwise. Behavior relied on by the human's "I can ask DoD status and the claim is backed by a file" requirement.

- [ ] **Step 1: Add `LEDGER_RE` next to the existing regexes**

In `.claude/hooks/dod-check.js`, immediately after the `EVIDENCE_RE` definition (currently line ~49), add:
```js
// A completion claim must point at the slice's DoD ledger (the queryable record of gate status).
const LEDGER_RE = /superpowers[\/\\]verification[\/\\][^\s)"']+[\/\\]dod\.md/i;
```

- [ ] **Step 2: Replace the decision logic to require the ledger citation**

Replace the existing block:
```js
  if (!CLAIM_RE.test(text)) process.exit(0);
  if (EVIDENCE_RE.test(text)) process.exit(0);
```
with:
```js
  if (!CLAIM_RE.test(text)) process.exit(0);
  // A completion claim is only allowed when it cites the DoD ledger for the slice.
  // The ledger is the mandated record of per-gate status (CLAUDE.md §Definition of Done);
  // evidence keywords alone no longer suffice.
  if (LEDGER_RE.test(text)) process.exit(0);
```

- [ ] **Step 3: Update the block `reason` to name the ledger + template**

In the `reason` array, replace the final line:
```js
    'Revise the claim to cite each bullet by command + output, or use "implementation staged, <step> pending verification" instead of "complete/done/ready to merge".',
```
with:
```js
    '',
    'Record each gate in the slice DoD ledger and CITE it in the claim:',
    '  docs/superpowers/verification/<date>-<topic>/dod.md',
    '  (copy docs/superpowers/templates/dod-ledger-template.md if it does not exist yet).',
    'Or, if not actually done, say "implementation staged, <step> pending verification" instead of "complete/done/ready to merge".',
```

- [ ] **Step 4: Write the test harness fixture**

Create a throwaway transcript + driver in the scratchpad (NOT committed). Run (Bash):
```
SP="$TMPDIR_CLAUDE"; mkdir -p /tmp/dodtest
mk() { printf '{"message":{"role":"assistant","content":[{"type":"text","text":%s}]}}\n' "$2" > "/tmp/dodtest/$1.jsonl"; }
run() { echo "{\"transcript_path\":\"/tmp/dodtest/$1.jsonl\"}" | node .claude/hooks/dod-check.js; echo "  <-- $1 (empty=allow)"; }
```
(Define `mk`/`run` helpers; `mk` takes a name + a JSON-encoded string.)

- [ ] **Step 5: Run the four cases and verify block vs allow**

Run (Bash, repo root):
```
mk claim_noledger '"Slice complete."'
mk claim_evidence_noledger '"Implementation complete. Build green, full test suite green."'
mk claim_withledger '"Implementation complete. Status in docs/superpowers/verification/2026-06-26-catalog-dependency-mini-graph/dod.md"'
mk notaclaim '"Here is the plan; nothing is finished yet."'
for c in claim_noledger claim_evidence_noledger claim_withledger notaclaim; do run "$c"; done
```
Expected:
- `claim_noledger` → prints `{"decision":"block",…}` (claim, no ledger).
- `claim_evidence_noledger` → prints `{"decision":"block",…}` (**new**: evidence keywords no longer suffice).
- `claim_withledger` → empty (allow — cites the ledger).
- `notaclaim` → empty (allow — `CLAIM_RE` no match).

If any case is wrong, fix the regex/logic and re-run before commit.

- [ ] **Step 6: Commit**

```
git add .claude/hooks/dod-check.js
git commit -m "feat(hooks): dod-check requires completion claims to cite the DoD ledger"
```

---

### Task 3: Mandate the ledger in `CLAUDE.md`

**Files:**
- Modify: `CLAUDE.md` (Definition of Done working agreement + "Where to find things" table)

**Interfaces:**
- Consumes: the template path (Task 1), the `verification/` location.
- Produces: the standing working agreement that future slices (and `writing-plans`) follow.

- [ ] **Step 1: Add the "Where to find things" row**

In `CLAUDE.md`, the "Where to find things" table, after the row for `Per-slice implementation specs & plans`, add:
```
| Per-slice verification proof (DoD ledger + reviews + evidence) | `docs/superpowers/verification/{date}-{topic}/` (entry point: `dod.md`) |
```

- [ ] **Step 2: Add the working-agreement bullet**

In `CLAUDE.md` under the Definition of Done section (right after the nine-gate list, before the "Terminal re-verify" paragraph), add:
```
  **DoD ledger (queryable status):** each slice maintains a DoD ledger at `docs/superpowers/verification/<date>-<topic>/dod.md` — copy `docs/superpowers/templates/dod-ledger-template.md` at slice start and update each gate's row the moment that gate runs (not just at close). Reviews, deep-review reports, and raw evidence (screenshots/logs) live as siblings in the same `verification/<date>-<topic>/` folder; `dod.md` is the index. A "what's the DoD status?" question is answered by reading that file's summary table. Completion claims MUST cite the ledger path — the `.claude/hooks/dod-check.js` stop hook blocks claims that don't.
```

- [ ] **Step 3: Verify both edits landed**

Run (Bash, repo root):
```
grep -n "verification/<date>-<topic>/dod.md" CLAUDE.md; grep -n "Per-slice verification proof" CLAUDE.md
```
Expected: at least one hit each.

- [ ] **Step 4: Commit**

```
git add CLAUDE.md
git commit -m "docs: mandate the per-slice DoD ledger + verification/ location"
```

---

### Task 4: Backfill the mini-graph slice ledger

The convention's first real instance, capturing PR #46's true status.

**Files:**
- Create: `docs/superpowers/verification/2026-06-26-catalog-dependency-mini-graph/dod.md`

**Interfaces:**
- Consumes: the template (Task 1).
- Produces: the queryable ledger for PR #46 — the answer to the original "what's the DoD status?" question.

- [ ] **Step 1: Use the mini-graph slice head sha for the header**

The ledger documents the mini-graph slice (E-04.F-02.S-01); its gate evidence (CI run 28236067701) corresponds to that slice's head commit `6b9bc3f` — **not** the current branch HEAD, which now also carries the DoD-convention commits. Use `6b9bc3f` wherever the header shows `<sha>`.

- [ ] **Step 2: Write the ledger**

Create `docs/superpowers/verification/2026-06-26-catalog-dependency-mini-graph/dod.md`:
```markdown
# DoD Ledger — Catalog Dependency Mini-Graph (E-04.F-02.S-01)

**Slice:** `2026-06-26-catalog-dependency-mini-graph` · **Branch:** `feat/catalog-dependency-mini-graph` · **HEAD:** `<sha>`
**PR:** [#46](https://github.com/diablo39/Roman.Kartova/pull/46) · **Last updated:** 2026-06-26
**Spec:** `docs/superpowers/specs/2026-06-26-catalog-dependency-mini-graph-design.md`
**Plan:** `docs/superpowers/plans/2026-06-26-catalog-dependency-mini-graph.md`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-06-26 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-06-26 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-06-26 |
| 4 Container build (images CI) | ✅ PASS | 2026-06-26 |
| 5 `/simplify` | ⏳ PENDING | 2026-06-26 |
| 6 Mutation (conditional) | N/A | 2026-06-26 |
| 7 `requesting-code-review` | ✅ PASS | 2026-06-26 |
| 8 `review-pr` | ⏳ PENDING | 2026-06-26 |
| 9 `deep-review` | ⏳ PENDING | 2026-06-26 |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | 2026-06-26 |
| Terminal re-verify (build + suite) | ⏳ PENDING | 2026-06-26 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS | 2026-06-26 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** CI run [28236067701](https://github.com/diablo39/Roman.Kartova/actions/runs/28236067701) — Frontend (typecheck+build) + Backend checks green.
**At:** PR #46 head, 2026-06-26

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Spec + quality reviews ran clean for all 5 implementation tasks during the original SDD session (recorded in the `.superpowers/sdd` controller ledger; corroborated by the commit chain `c9a8ec3..1ff557b`). Task 3 fixed a TS2698 spread-cast pre-review.
**At:** PR #46 head, 2026-06-26

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 — Frontend (test+typecheck+build) + Backend (arch+unit+integration) green. Real-seam **N/A** — frontend-only slice, no HTTP/auth/DB wiring.
**At:** PR #46 head, 2026-06-26

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 — "Container images (build — Dockerfile/restore gate)" check green (web image restores `@xyflow/react`).
**At:** PR #46 head, 2026-06-26

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### 6 — Mutation loop (conditional)
**Status:** N/A
**Evidence:** No C# Domain/Application change — frontend-only slice. Mutation gate not applicable.
**At:** 2026-06-26

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Final whole-branch code review (superpowers:requesting-code-review) returned ready-to-merge-with-fixes, no Critical/Important findings; the one fix (untested focused-node style branch) landed in `6b9bc3f`. (A subagent review — not posted to the GitHub PR, hence PR #46 shows 0 reviews.)
**At:** PR #46 head, 2026-06-26

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### Manual / Playwright verification (ADR-0084)
**Status:** ⏳ PENDING
**Evidence:** UI slice — cold-start Playwright pass (graph render + node-click nav + empty state) not recorded.
**At:** 2026-06-26

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** Pending gates 5/7/8/9.
**At:** 2026-06-26

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 green on PR #46 (all 5 jobs) — the runner is the mirror's source of truth.
**At:** PR #46 head, 2026-06-26
```
Replace `<sha>` with `6b9bc3f` (the mini-graph slice head, per Step 1).

- [ ] **Step 3: Verify it parses and reflects reality**

Run (Bash, repo root):
```
grep -E 'PASS|PENDING|N/A' docs/superpowers/verification/2026-06-26-catalog-dependency-mini-graph/dod.md | head -14
```
Expected: summary rows print with the statuses from Step 2 (4× PASS, 1× N/A, rest PENDING).

- [ ] **Step 4: Commit**

```
git add docs/superpowers/verification/2026-06-26-catalog-dependency-mini-graph/dod.md
git commit -m "docs(superpowers): backfill DoD ledger for the mini-graph slice (PR #46)"
```

---

### Task 5: Migrate `reviews/` + `evidence/` into `verification/`, repoint references

One isolated cleanup commit. Move every existing artifact into a per-slice `verification/<date>-<topic>/` folder and fix all live references.

**Files:**
- Move (git mv): all of `docs/superpowers/reviews/*` and `docs/superpowers/evidence/*`
- Modify: `.claude/commands/deep-review.md`, `.github/copilot/skills/deep-review/SKILL.md`, `docs/TESTING-STRATEGY.md`
- Modify (via Serena, `.cs`): `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`
- Modify (find/replace): the historical plan/spec files that self-reference old `evidence/` paths

**Interfaces:**
- Consumes: nothing (mechanical move).
- Produces: a single `verification/` tree; `reviews/` and `evidence/` deleted; no dangling `superpowers/(reviews|evidence)` paths.

- [ ] **Step 1: Repoint the deep-review output path (live tooling)**

In `.claude/commands/deep-review.md`, change the two occurrences of
`docs/superpowers/reviews/YYYY-MM-DD-<branch-or-pr>-review.md` and `docs/superpowers/reviews/` to point at
`docs/superpowers/verification/<date>-<topic>/deep-review.md` (and "Create `docs/superpowers/verification/<date>-<topic>/` if it does not exist").

In `.github/copilot/skills/deep-review/SKILL.md`, make the identical change to its two occurrences.

- [ ] **Step 2: Execute the move (git mv)**

Run (Bash, repo root). Each line creates the slice folder and moves files in, renaming to drop the redundant prefix:
```
cd docs/superpowers
mvf() { mkdir -p "verification/$1"; git mv "$2" "verification/$1/$3"; }   # folder, src, newname

# --- reviews/ (38 files) → grouped per slice ---
mvf 2026-04-30-feat-slice-3-catalog-application      reviews/2026-04-30-feat-slice-3-catalog-application-review.md           review.md
mvf 2026-05-04-sorting-pagination                    reviews/2026-05-05-feat-sorting-pagination-review.md                    review.md
mvf 2026-05-07-feat-slice-5-applications-edit-lifecycle reviews/2026-05-07-feat-slice-5-applications-edit-lifecycle-review.md review.md
mvf 2026-05-07-feat-slice-6-phase-1-cleanup          reviews/2026-05-07-feat-slice-6-phase-1-cleanup-review.md                review.md
for p in 0 1 2 3 4 5 6 7 8 9 10 11 12; do \
  mkdir -p verification/2026-05-09-feat-mstest-migration; \
  git mv reviews/2026-05-0?-feat-mstest-migration-phase-$p-review.md verification/2026-05-09-feat-mstest-migration/phase-$p-review.md 2>/dev/null || \
  git mv reviews/2026-05-09-feat-mstest-migration-phase-$p-review.md verification/2026-05-09-feat-mstest-migration/phase-$p-review.md; done
mvf 2026-05-22-slice-7  reviews/2026-05-22-slice-7-deep-review.md                     deep-review.md
mvf 2026-05-22-slice-7  reviews/2026-05-22-slice-7-docker-smoke.md                    docker-smoke.md
mvf 2026-05-22-slice-7  reviews/2026-05-22-slice-7-rbac-roles-reverse-lifecycle-review.md  rbac-roles-reverse-lifecycle-review.md
mvf 2026-05-26-slice-8-team-management  reviews/2026-05-26-slice-8-team-management-deep-review.md  deep-review.md
mvf 2026-05-29-slice-9  reviews/2026-05-29-slice-9-deep-review.md      deep-review.md
mvf 2026-05-29-slice-9  reviews/2026-05-29-slice-9-dod-evidence.md     dod-evidence.md
mvf 2026-05-29-slice-9  reviews/2026-06-01-slice-9-deep-review-rerun.md  deep-review-rerun.md
mvf 2026-06-01-cursorcodec-filter-generalization  reviews/2026-06-01-cursorcodec-filter-generalization-review.md  review.md
mvf 2026-06-01-invitation-set-password-flow        reviews/2026-06-01-invitation-set-password-flow-review.md       review.md
mvf 2026-06-10-slice-10-member-lifecycle  reviews/2026-06-10-slice-10-member-lifecycle-review.md  review-2026-06-10.md
mvf 2026-06-10-slice-10-member-lifecycle  reviews/2026-06-11-slice-10-member-lifecycle-review.md  review-2026-06-11.md
mvf 2026-06-15-feat-audit-log-foundation  reviews/2026-06-15-feat-audit-log-foundation-review.md  review.md
mvf 2026-06-16-feat-audit-checkpoints      reviews/2026-06-16-feat-audit-checkpoints-deep-review.md  deep-review.md
mvf 2026-06-17-feat-audit-event-wiring     reviews/2026-06-17-feat-audit-event-wiring-review.md      review.md
mvf 2026-06-18-feat-audit-system-actor-sweep  reviews/2026-06-18-feat-audit-system-actor-sweep-review.md  review.md
mvf 2026-06-19-feat-audit-catalog-event-wiring  reviews/2026-06-19-feat-audit-catalog-event-wiring-review.md  review.md
mvf 2026-06-20-feat-catalog-service-ui-surface  reviews/2026-06-20-feat-catalog-service-ui-surface-review.md  review.md
mvf 2026-06-22-feat-list-filter-surface-catalog  reviews/2026-06-22-feat-list-filter-surface-catalog-review.md  review.md
mvf 2026-06-23-feat-filter-surface-members-single-select  reviews/2026-06-23-feat-filter-surface-members-single-select-review.md  review.md
mvf 2026-06-24-feat-applications-filter-team-lifecycle  reviews/2026-06-24-feat-applications-filter-team-lifecycle-review.md  review.md
mvf 2026-06-24-PR42-catalog-relationships  reviews/2026-06-24-PR42-catalog-relationships-review.md  review.md

# --- evidence/ (5 folders) → move contents into the matching slice folder ---
mkdir -p verification/2026-04-30-slice-4 && git mv evidence/2026-04-30-slice-4/* verification/2026-04-30-slice-4/
mkdir -p verification/2026-05-01-untitled-ui-migration && git mv evidence/2026-05-01-untitled-ui-migration/* verification/2026-05-01-untitled-ui-migration/
git mv evidence/2026-05-04-sorting-pagination/* verification/2026-05-04-sorting-pagination/
mkdir -p verification/2026-06-09-team-admin-membership-authority && git mv evidence/2026-06-09-team-admin-membership-authority/* verification/2026-06-09-team-admin-membership-authority/
mkdir -p verification/2026-06-12-audit-log-foundation && git mv evidence/2026-06-12-audit-log-foundation/* verification/2026-06-12-audit-log-foundation/

# evidence pairs share a folder with their review where dates differ:
#   sorting-pagination review (05-05) already created 2026-05-04-sorting-pagination above.
#   audit-log-foundation review (06-15) and evidence (06-12) → fold review into the 06-12 folder:
git mv verification/2026-06-15-feat-audit-log-foundation/review.md verification/2026-06-12-audit-log-foundation/review.md && rmdir verification/2026-06-15-feat-audit-log-foundation 2>/dev/null || true

cd ../..
```
Note: the `mvf` for `2026-06-15-feat-audit-log-foundation` runs in Step 2's reviews block and is then folded into the `2026-06-12-audit-log-foundation` evidence folder by the last two commands. After this, `reviews/` and `evidence/` should be empty.

- [ ] **Step 3: Remove the now-empty old dirs**

Run (Bash, repo root):
```
rmdir docs/superpowers/reviews docs/superpowers/evidence/2026-* docs/superpowers/evidence 2>/dev/null; ls docs/superpowers/
```
Expected: `reviews` and `evidence` are gone; `verification`, `specs`, `plans`, `templates` remain. If `rmdir` reports "not empty", a file was missed — `ls` the offender and add a `git mv` for it.

- [ ] **Step 4: Repoint the two active cross-links**

`docs/TESTING-STRATEGY.md:92` — change `docs/superpowers/reviews/2026-05-09-feat-mstest-migration-phase-9-review.md` to `docs/superpowers/verification/2026-05-09-feat-mstest-migration/phase-9-review.md` (built-in `Edit`; markdown).

`tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs:132` — the `<c>…</c>` doc comment. Use **Serena** `replace_content` (existing `.cs`): needle `docs/superpowers/reviews/2026-05-09-feat-mstest-migration-phase-9-review.md`, repl `docs/superpowers/verification/2026-05-09-feat-mstest-migration/phase-9-review.md`. Then CRLF-normalize the file (`sed -i 's/\r$//' tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`) and confirm `git diff --stat` equals `git diff -w --stat` for it (only the one comment line changed, no line-ending churn).

- [ ] **Step 5: Repoint historical plan/spec self-references (find/replace)**

These point at old `evidence/` paths inside completed slices' plans/specs. Update each to the new `verification/<date>-<topic>/` path (built-in `Edit`; all markdown):
- `docs/superpowers/plans/2026-05-01-untitled-ui-migration-plan.md` (lines ~973–977, 1019, 1068): `evidence/2026-05-01-untitled-ui-migration/` → `verification/2026-05-01-untitled-ui-migration/`
- `docs/superpowers/specs/2026-05-01-untitled-ui-migration-design.md` (lines ~124, 173): same replacement (and the `2026-05-XX`/`<date>-untitled-ui-migration` variants → `verification/2026-05-01-untitled-ui-migration/`)
- `docs/superpowers/plans/2026-04-30-slice-4-catalog-ui-first-cut-plan.md` (lines ~1890, 1903, 1963): `evidence/2026-04-30-slice-4/` → `verification/2026-04-30-slice-4/`
- `docs/superpowers/plans/2026-05-04-sorting-pagination-plan.md` (lines ~2804, 2813): `evidence/2026-05-04-sorting-pagination/` → `verification/2026-05-04-sorting-pagination/`
- `docs/superpowers/plans/2026-06-09-team-admin-membership-authority-plan.md` (line ~431): `evidence/2026-06-09-team-admin-membership-authority/` → `verification/2026-06-09-team-admin-membership-authority/`
- `docs/superpowers/plans/2026-06-12-audit-log-foundation-plan.md` (lines ~1721, 1744, 1757): `evidence/2026-06-12-audit-log-foundation/` → `verification/2026-06-12-audit-log-foundation/`

- [ ] **Step 6: Verify no dangling references remain**

Run (Bash, repo root):
```
grep -rEn 'superpowers/(reviews|evidence)' . --include='*.md' --include='*.cs' --include='*.js' 2>/dev/null | grep -v 'docs/superpowers/plans/2026-06-26-dod-ledger-convention.md' | grep -v 'docs/superpowers/specs/2026-06-26-dod-ledger-convention-design.md'
```
Expected: **no output** (the only allowed mentions are this plan + its spec, which describe the migration). Any other hit is a missed reference — fix it.

- [ ] **Step 7: Confirm git sees the moves as renames (history preserved)**

Run (Bash, repo root):
```
git status --short | grep -E '^R' | wc -l; git status --short | grep -vE '^R' | grep -E 'reviews/|evidence/'
```
Expected: a large rename count (`R…`); the second command prints nothing (no add/delete pairs that lost history). For the `.cs`, `git diff -w --stat` shows the single comment line.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "refactor(superpowers): consolidate reviews/ + evidence/ into per-slice verification/ tree"
```

- [ ] **Step 9: Slice close — DoD ledger for THIS work + gates**

This convention work is itself a slice. Create its ledger `docs/superpowers/verification/2026-06-26-dod-ledger-convention/dod.md` from the template and fill rows as gates run. Slice-specific gate applicability:
- Gate 1 build / 3 suite / 4 container: the only code change is the `.cs` doc-comment + the `.js` hook (not compiled into the solution). Re-run `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` after Step 4's `.cs` edit to confirm green; tests unaffected.
- Gate 6 mutation: **N/A** (no Domain/Application logic).
- Manual/Playwright: **N/A** (no UI).
- Hook behavior: cite Task 2 Step 5's four-case result as the gate-appropriate evidence.
- Gates 5/7/8/9: run against the branch diff per CLAUDE.md.

---

## Self-Review

**1. Spec coverage:**
- §3 verification/ convention (folder per slice, dod.md index) → Task 4 (creates the first one) + Task 5 (migrates the rest). ✓
- §4 ledger schema (summary table + per-gate evidence, current gate list) → Task 1 template + Task 4 instance. ✓
- §5 template file → Task 1. ✓
- §6 hook enforcement (LEDGER_RE, block without citation) → Task 2 (with 4-case test). ✓
- §7 CLAUDE.md (working agreement + where-to-find row + writing-plans note) → Task 3. ✓ (writing-plans note covered by the CLAUDE.md mandate the plans link to; no skill-file edit needed.)
- §8 migration + repoint live tooling + active cross-links + historical refs + .cs wrinkle → Task 5 steps 1/4/5 + CRLF note. ✓
- §9 backfill mini-graph ledger → Task 4. ✓
- §10 testing (hook stdin cases, migration grep, .cs build) → Task 2 step 5, Task 5 step 6, Task 5 step 9. ✓

**2. Placeholder scan:** Template/ledger/hook show full content; migration shows the explicit move map; each step has an expected result. `<sha>` in Task 4 is resolved by Task 4 step 1 (not a placeholder — a computed value). No TBD/TODO. ✓

**3. Type/path consistency:**
- `LEDGER_RE` (Task 2) matches `verification/<…>/dod.md` — the exact path Task 4 creates and Task 3 mandates. ✓
- mstest-phase-9 new path used identically in Task 5 step 4 (both the `.md` and `.cs` cross-link) and produced by the loop in Task 5 step 2 (`verification/2026-05-09-feat-mstest-migration/phase-9-review.md`). ✓
- Folder names in the move map (Task 5 step 2) are `<date>-<topic>` slugs; `dod.md` filename constant across Tasks 1/4. ✓

No issues found.
