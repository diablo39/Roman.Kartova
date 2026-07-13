# Gate 8 — `review-pr` (pr-review-toolkit)

**Branch:** `feat/catalog-tabbed-entity-detail` · **Reviewed at:** c905bcd · **Fixes at:** 89ce200
Three focused lenses run in parallel (code-reviewer, pr-test-analyzer, type-design-analyzer). Ran for real per the no-folding rule — it surfaced two genuine should-fix items the whole-branch (gate 7) and deep-review (gate 9) passes did not.

## code-reviewer
No Critical/Important. Verified the four `/simplify` fixes present in `c905bcd`; all in-panel react-aria `<Table>`s carry `isRowHeader` (ADR-0084). Suggestions (not applied — cosmetic): a11y — `TabList` reuses entity displayName as accessible name; one-frame lag before invalid `?tab` normalizes (matches tests). Strengths: clean compound primitive, `?tab=` contract exactly per ADR-0114, per-entity tab sets differ correctly.

## pr-test-analyzer
- **Rating 8 (real gap, FIXED):** `ApiDetailPage` never asserted the Definition tab at page level — a mis-wired tab id (the S-04 goal) would ship silently. Added `renders the spec document on the Definition tab` (`?tab=definition` → Definition selected + `"No spec attached."`). Fixed in `89ce200`.
- Rating 6 (not applied): replace-vs-push not asserted (back-button hygiene, not correctness).
- Rating 4 (not applied): `ApiSurfaceSection` heading not asserted on Service/App Dependencies (empty fixture → mount-only).
- No tautological tests found; `getByText("Incoming")` and the `getAllByRole("rowheader")` guard are load-bearing.

## type-design-analyzer
- **Real gap (FIXED):** `Children.toArray(children).filter(isValidElement) as ReactElement<DetailTabProps>[]` was an unchecked cast — any element passed, a stray child rendering a blank tab. Tightened to a type-guard `(c): c is ReactElement<DetailTabProps> => isValidElement(c) && c.type === DetailTab`. Fixed in `89ce200`.
- Not applied: dev-only duplicate-`id` throw (nice-to-have; blast radius small for an internal primitive), richer `label` type.
- Verdict: solid idiomatic compound-component; the type-guard closes its one real soundness gap.

## Outcome
2 should-fix applied (`89ce200`), 9/9 targeted tests + full suite 824/824 green. Remaining suggestions are cosmetic/nice-to-have, triaged out with reasons above.
