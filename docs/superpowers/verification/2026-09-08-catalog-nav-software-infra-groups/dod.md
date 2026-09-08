# DoD Ledger — Catalog nav Software/Infrastructure split

**Slice:** `2026-09-08-catalog-nav-software-infra-groups` · **Branch:** merged to `master` · **HEAD:** `6992e5ab`
**PR:** none — direct to local `master` (owner local-master workflow, gates 10 waiver below) · **Last updated:** 2026-09-08
**Spec:** none — bounded UI-only slice (brainstorming bounded path, no spec/plan doc; nav IA confirmed via interactive mockup artifact)
**Plan:** N/A (bounded)
**Findings telemetry:** none created — diff is 2 frontend files, findings recorded inline below.

> Records the Definition of Done from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (reason) · WAIVER (owner).
> Scope: bounded, **frontend-only** (`web/src/components/layout/Sidebar.tsx` + its test). No C#, no HTTP/auth/DB/middleware, no Dockerfile/deps change.

## Summary

| Gate | Status | Note |
|------|--------|------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ / N/A | Backend N/A (no C#). Frontend `npm run build` (tsc+vite) exit 0. |
| 2 Per-task subagent reviews | ✅ | `typescript-code-reviewer`: 0 S0/S1 in shipped code. S1 test-gap + S2 aria-controls fixed same-day (`6992e5ab`). 3 S3 = follow-ups. + inline simplify. |
| 3 Full suite (+ real-seam if wiring) | ✅ | Frontend 1027/1027 (1 load-flake, passed isolated); Sidebar **21/21** (after review fixes). Real-seam N/A — no wiring surface. |
| 4 Container build (images CI) | N/A→CI | No Dockerfile/deps change; web image rebuild deferred to CI (gate 10). Not run locally. |
| 5 `/simplify` | ✅ | 2 fixes applied (pure toggle, drop unused testid). |
| 6 `requesting-code-review` | ⬜ WAIVER? | Owner decision (see below). |
| 7 `review-pr` | ⬜ WAIVER? | Owner decision. |
| 8 `deep-review` | ⬜ WAIVER? | Owner decision. |
| Terminal re-verify (build + suite) | ✅ (partial) | Sidebar 19/19 re-run after simplify; full-suite delta = 2 lines in one already-covered component. |
| 9 Visual / API verification (ADR-0084) | ✅ | Live on 5173 via claude-in-chrome: structure + collapse/expand + disabled leaves + active-highlight; screenshot captured. |
| 10 CI green on PR (`ci-local.sh` mirror) | ⬜ WAIVER? | Not pushed — owner local-master workflow. Owner decision. |

## Gate detail

### 1 — Build
Backend **N/A** (no C# in diff). Frontend: `npm run build` → `built in 2m 26s`, exit 0 (tsc type gate + vite). At `017c4f90`.

### 2 — Per-task subagent reviews
Inline `/simplify` 4-angle review done (findings in gate 5). `typescript-code-reviewer` subagent ran the gates itself (eslint 0, tsc no Sidebar errors, vitest 19/19) — verdict **pass, 0 S0/S1 in shipped code**. Findings actioned:
- **S1** (test-adequacy): guarded `sessionStorage` throw-paths untested → added coverage (default-open fallback + toggle-still-works). Fixed `6992e5ab`.
- **S2** (a11y): no `aria-controls` linking toggle→list → added `id`+`aria-controls`, list now always rendered (`hidden` when collapsed). Fixed `6992e5ab`.
- **S2** (process): manual keyboard/screen-reader pass not performed — see gate 9 note.
- **S3 ×3** deferred as follow-ups: `DisabledItem` lacks `aria-disabled` (pre-existing, surface doubled 2→4 leaves); `queryByRole+toBeInTheDocument` style nit; disclosure-icon inconsistency vs `HierarchyTreeNode` (text glyph vs SVG chevron).
Re-verified Sidebar 21/21 after fixes.

### 3 — Full test suite
`npx vitest run` → 1027/1027 pass (1 file timed out under load-contention, `RegisterServiceDialog`, passed 9/9 isolated). `Sidebar.test.tsx` 19/19. **Real-seam N/A** — pure presentational component, no HTTP/auth/DB/middleware. New tests assert real behavior: collapse hides member links, sessionStorage round-trips, active-highlight survives, disabled-leaf semantics.

### 4 — Container build
**N/A locally** — no Dockerfile/`COPY`/dependency change; the change is a `.tsx` bundled by the existing web image. A `docker compose build web` would rebuild it; deferred to CI on push (gate 10).

### 5 — `/simplify`
Ran inline (4 angles; 4 parallel agents skipped as disproportionate for a 150-line diff + a reviewer already running). Findings + fixes:
- **Simplification:** `toggle` wrote to `sessionStorage` inside the setState updater (impure, double-writes under StrictMode) → made pure (compute `next`, `setOpen(next)`, write outside). Applied.
- **Simplification:** unused `nav-collapsible-<key>-items` testid → removed. Applied.
- Reuse / Efficiency / Altitude: clean (Sidebar hand-builds its own primitives by convention; no shared collapsible to reuse).
Re-ran Sidebar tests after fixes → 19/19.

### 6–8 — requesting-code-review / review-pr / deep-review
**Not run.** For a bounded 150-line UI diff these slice-boundary review skills are heavyweight; per the no-folding rule they are NOT covered by gates 2/5. Left as an explicit **owner-waiver decision** (below), not marked green.

### Terminal re-verify
Sidebar **21/21** re-run after the simplify + gate-2 review edits (`6992e5ab`). Full suite not re-run in full — delta since the green full-suite run is confined to `Sidebar.tsx` + its test, fully covered by the re-run Sidebar suite; `npm run build` (tsc) green earlier and the reviewer independently ran `tsc --noEmit` clean on the file.

### 9 — Visual / API verification
Driven live on the running stack (`http://localhost:5173/catalog/applications`, logged-in Org A) via `claude-in-chrome`. Verified: two collapsible groups render (Software expanded with Applications[active]/Services/APIs/Systems; Infrastructure expanded with disabled Components/Brokers); clicking Software collapses it (chevron rotates, member links hidden) while Infrastructure stays open; Hierarchy + Docs at Catalog level. Screenshot: `screenshot-1788865030572-0.jpg` (collapsed state). Verified on commit `017c4f90`; the later `hidden`-attr swap (`6992e5ab`) is visually identical (`display:none` ≡ unmount) so not re-driven. **Outstanding (reviewer S2):** no keyboard-only / screen-reader spot-check of the new disclosure buttons was performed — jsdom confirms `aria-expanded`/`aria-controls` toggle but not that AT announces state sensibly. Recommended before final sign-off.

### 10 — CI green on PR
**Not pushed.** Owner local-master workflow (prior slices, e.g. E-03.F-03.S-02, waived gate 10 the same way). `scripts/ci-local.sh` not run. **Owner-waiver decision** (below).
