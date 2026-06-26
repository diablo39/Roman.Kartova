# DoD Ledger Convention + Verification-Artifact Consolidation — Design

**Date:** 2026-06-26
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (brainstorming) — pending spec review
**Topic slug:** `dod-ledger-convention`

## 1. Problem

Two gaps, one root cause (verification artifacts are scattered and ad-hoc):

1. **DoD status is not queryable.** When asked "what's the DoD status of slice X?", there is no single file to read. The answer must be reconstructed from git history, CI runs, and PR review state. The mini-graph slice (PR #46) is a live example: gates 1/3/4 are green in CI (run `28236067701`) but gates 5/7/8/9 + the ADR-0084 Playwright pass have no recorded status anywhere.
2. **Artifact sprawl.** Verification proof lives in two parallel, overlapping locations:
   - `docs/superpowers/reviews/` — 38 flat `.md` report files (`-review`, `-deep-review`, one `-dod-evidence`, one `-docker-smoke`).
   - `docs/superpowers/evidence/<date>-<slice>/` — 5 per-slice folders of raw artifacts: screenshots (`.png`), console logs, curl/db output, mutation-evidence notes.

   These were never poolable into one flat directory: screenshots force a per-slice folder, while reports are flat files. The result is "where's the proof for slice X?" having two answers.

There is one prior instance of the artifact we want — `reviews/2026-05-29-slice-9-dod-evidence.md` — but it was a one-off, used the now-stale 9-bullet DoD numbering, and never became a standing convention.

## 2. Goals / Non-goals

**Goals**
- A single, live, per-slice **DoD ledger** (`dod.md`) that records the status + citable evidence of every Definition-of-Done gate, updated incrementally as each gate runs, so DoD status is readable at any point in a slice.
- Consolidate `reviews/` + `evidence/` into **one per-slice folder** with the ledger as its index/table-of-contents.
- Enforce that completion claims cite the ledger, via the existing `dod-check.js` stop hook.
- Migrate existing artifacts into the new layout and repoint all live references.

**Non-goals**
- Changing what the DoD gates *are* — the gate list is owned by `CLAUDE.md §Definition of Done`; this convention only *records* them.
- Touching `specs/` and `plans/` — they are authoring **inputs**, not proofs; they keep their flat, prefix-keyed layout.
- Automating gate execution. The ledger is written by whoever runs the gate (Claude or human); this design covers the artifact + enforcement, not orchestration.

## 3. The `verification/` convention

Replace `reviews/` and `evidence/` with a single per-slice folder tree:

```
docs/superpowers/
  specs/   2026-06-26-<topic>-design.md      ← input, unchanged
  plans/   2026-06-26-<topic>.md             ← input, unchanged
  verification/                               ← NEW (merges reviews/ + evidence/)
    2026-06-26-<topic>/
      dod.md            ← ledger + index; the file a "status?" query reads
      deep-review.md    ← report files (siblings, linked from dod.md)
      review-pr.md
      simplify.md
      requesting-code-review.md
      playwright/        ← screenshots, console logs (raw evidence)
      db-verification.md, curl-output.md, mutation-evidence.md  ← as needed
  templates/
    dod-ledger-template.md   ← NEW
    pr-deep-review-prompt.md ← unchanged
```

- **Folder name** = `<date>-<topic>`, identical to the slice's design/plan slug (`2026-06-26-catalog-dependency-mini-graph`). One folder ⇒ one slice ⇒ one place for all its proof.
- **`dod.md` is the entry point.** Report siblings are linked from it; raw evidence (screenshots/logs) lives in the same folder (or a `playwright/` subfolder).
- Files inside drop the redundant date/slice prefix (`deep-review.md`, not `2026-06-26-<topic>-deep-review.md`) — the folder already carries the key.

## 4. The `dod.md` ledger schema

Fixed schema, tracking the **current** `CLAUDE.md` gates (1–9, gate 6 conditional) plus the terminal re-verify and pre-push CI mirror. Explicitly **not** the stale slice-9 9-bullet numbering.

**Header**
- Slice / topic, target branch + HEAD commit, PR link, last-updated date.

**Summary table (top of file — this is what a status query reads):**

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ / ❌ / ⏳ / N/A | 2026-06-26 |
| 2 Per-task subagent reviews | … | … |
| 3 Full suite (+ real-seam if wiring) | … | … |
| 4 Container build (images CI) | … | … |
| 5 `/simplify` | … | … |
| 6 Mutation (conditional) | … | … |
| 7 `requesting-code-review` | … | … |
| 8 `review-pr` | … | … |
| 9 `deep-review` | … | … |
| Manual / Playwright (ADR-0084) | … | … |
| Terminal re-verify (build + suite) | … | … |
| Pre-push CI mirror (`ci-local.sh`) | … | … |

**Per-gate section** (one per row) records:
- **Status** — `✅ PASS` / `❌ FAIL` / `⏳ PENDING` / `N/A` (N/A and FAIL **must** carry a one-line reason).
- **Evidence** — exactly one of: command + output excerpt · CI run URL · PR review / subagent id · link to a sibling report file (`deep-review.md`, screenshots).
- **Commit / date** the status was recorded at.

**Status legend** block so the symbols are unambiguous.

A "DoD status?" query is answered by reading the summary table; drill-down reads the section + linked sibling.

## 5. Template

`docs/superpowers/templates/dod-ledger-template.md` — the empty ledger with all rows present, every status pre-set to `⏳ PENDING`, placeholders for header fields. Slices copy it to `verification/<date>-<topic>/dod.md` at slice start (first gate run at the latest).

## 6. Enforcement — extend `.claude/hooks/dod-check.js`

The stop hook already blocks completion claims (`CLAIM_RE`) that lack verification-evidence keywords (`EVIDENCE_RE`). Add a **ledger-citation requirement**:

- New `LEDGER_RE` matching a ledger path: `docs/superpowers/verification/<…>/dod.md` (i.e. `verification/.+/dod\.md`).
- New block condition: when `CLAIM_RE` matches and the message does **not** cite a ledger path, block with a reason that points at the template and the new `verification/` location — even if evidence keywords are present. The ledger is the mandated artifact; citing it is how status stays current.
- Keep the existing evidence-keyword guidance in the block reason (unchanged gate list text, updated to mention the ledger).

This is the only executable change in the design.

## 7. `CLAUDE.md` changes

1. **New working-agreement bullet** under Definition of Done: each slice maintains a DoD ledger — copy `templates/dod-ledger-template.md` → `verification/<date>-<topic>/dod.md` at slice start; update each gate's row the moment it runs; completion claims cite the ledger; the `dod-check.js` stop hook enforces it.
2. **"Where to find things" table:** add a row — *Per-slice verification proof (DoD ledger + reviews + evidence) → `docs/superpowers/verification/<date>-<topic>/`*.
3. **`writing-plans` integration:** the plan's closing DoD step references "update the ledger row" per gate; the writing-plans self-review ensures a "create DoD ledger from template" task exists. (No skill-file edit required — this is captured by the CLAUDE.md mandate the plans already link to.)

## 8. Migration of existing artifacts

Move `reviews/*` and `evidence/*` into `verification/<date>-<topic>/`, grouping by slice key, and repoint references.

**Grouping** — each existing artifact maps to one `verification/<date>-<topic>/` folder by its date+slice/topic identifier. Multiple files for one slice (e.g. slice-9's `deep-review`, `dod-evidence`, `deep-review-rerun`) land in the same folder; inner filenames drop the prefix. The exact file→folder mapping table is enumerated in the implementation plan (writing-plans), not here.

**References to repoint (mandatory — these are live or active):**
- `.claude/commands/deep-review.md` (lines ~68, 70) — output path → `verification/<date>-<topic>/deep-review.md`.
- `.github/copilot/skills/deep-review/SKILL.md` (lines ~71, 73) — same.
- `docs/TESTING-STRATEGY.md:92` — cross-link to the mstest-phase-9 review file → new path.
- `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs:132` — **`.cs` doc-comment** cross-link → new path. **Wrinkle:** editing this `.cs` goes through Serena (serena-guard) and risks the known LF→CRLF flip on this host (normalize + verify `--stat == -w --stat` per the project memory). Because it is only a doc-comment link, an alternative is to leave it pointing at the historical path; the plan will pick one explicitly. Recommendation: update it (clean tree) with CRLF normalization.

**Historical plan/spec self-references** (completed slices' `evidence/` paths in `plans/2026-04-30-slice-4-…`, `plans/2026-05-01-untitled-ui-migration-…`, `plans/2026-05-04-sorting-pagination-…`, `plans/2026-06-09-…`, `plans/2026-06-12-…`, and the matching specs): rewritten by find/replace during the move so the tree has no dangling `superpowers/(reviews|evidence)` paths. These are descriptive records, low-risk to rewrite.

**Done-when (migration):** `grep -rE 'superpowers/(reviews|evidence)'` over the repo returns **only** intended hits (none, ideally), `reviews/` and `evidence/` directories are gone, and `git mv` preserved history.

## 9. Backfill the mini-graph slice

Create `verification/2026-06-26-catalog-dependency-mini-graph/dod.md` from the template with the evidence already gathered:

| Gate | Status | Evidence |
|------|--------|----------|
| 1 Build | ✅ PASS | CI run `28236067701` — Frontend + Backend checks |
| 2 Per-task reviews | ✅ PASS | SDD per-task spec+quality reviews ran clean (controller ledger + commit chain `c9a8ec3..1ff557b`) — upgraded from PENDING on later-found evidence |
| 3 Full suite | ✅ PASS | CI Frontend (test+typecheck+build) + Backend (arch+unit+integration). Real-seam **N/A** (frontend-only) |
| 4 Container build | ✅ PASS | CI "Container images" check |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation | N/A | no C# Domain/Application change |
| 7 `requesting-code-review` | ✅ PASS | final whole-branch review ready-to-merge, no Critical/Important, fix `6b9bc3f` — subagent review, not posted to GitHub PR (hence PR #46 shows 0 reviews); upgraded from PENDING on later-found evidence |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | UI slice — not recorded |
| Terminal re-verify | ⏳ PENDING | — |
| Pre-push CI mirror | ✅ PASS (inferred) | CI green on PR #46 (the runner is the mirror's source of truth) |

This is the convention's first real instance and makes PR #46's true status queryable.

## 10. Testing strategy

- **Hook (`dod-check.js`)** — the only executable artifact. Verify by piping crafted stdin JSON at a fake transcript file:
  - completion claim + **no** ledger citation → hook emits `{"decision":"block", …}`.
  - completion claim + a `verification/<…>/dod.md` citation → hook exits 0 (allow).
  - non-claim text → exits 0.
  Capture these runs in the slice's own `dod.md` (gate-appropriate evidence for a hook change).
- **Migration** — the done-when grep (no stray `superpowers/(reviews|evidence)` paths) is the verification; spot-check that `git mv` preserved file history (`git log --follow`).
- **No build/test-suite impact** otherwise: this is docs + one hook + one `.cs` doc-comment. The `.cs` edit must keep the solution build green (gate 1) — trivial, but re-run build after the comment edit.

## 11. Scope / size

Docs + 1 hook edit + 1 `.cs` doc-comment + a one-time `git mv` migration. No production business code. Well under the slice ceiling. The migration is mechanical (file moves + find/replace), isolated in one cleanup commit, separate from the convention/backfill commits.
