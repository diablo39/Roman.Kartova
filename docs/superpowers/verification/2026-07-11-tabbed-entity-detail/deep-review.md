# Deep review — Tabbed entity-detail layout (E-11.F-02.S-04)

**Branch:** `feat/catalog-tabbed-entity-detail` · **Status:** OPEN (pre-merge gate)
**Reviewed:** 2026-07-11 · full branch diff vs master (6 commits, HEAD `c905bcd`)
**Read against:** design `2026-07-11-catalog-tabbed-entity-detail-design.md`, plan `2026-07-11-catalog-tabbed-entity-detail.md`, ADR-0114/0094/0084/0095, ADR-0097 taxonomy, CLAUDE.md §Definition of Done.

---

### Overview

A frontend-only slice that adds a shared `DetailTabs` primitive (react-aria `Tabs`, ADR-0094) and splits the three catalog detail pages into tabs — API: Overview · Dependencies · Definition; Service/Application: Overview · Dependencies — with the API spec render moved off the Overview scroll onto a lazy Definition tab. The implementation faithfully matches the design's tab sets, `?tab=` semantics (default `overview`, `replace` writes, invalid deep-link normalized), header-above-tabs placement, and the ADR-0084 `isRowHeader` mitigation; the four /simplify fixes in commit `c905bcd` are present in the final `detail-tabs.tsx`. The code is correct and well-covered by unit tests; the one real gap is DoD **evidence**, not code — the CHECKLIST marks the story done citing a `dod.md` ledger that does not exist and gate-10 evidence is a single screenshot.

---

### Blocking-class issues (fail DoD)

**B1 — DoD ledger cited but absent; gate-10 evidence is a single screenshot.**
`docs/product/CHECKLIST.md` (E-11.F-02.S-04 row, added at diff line 90) flips the story to `[x]` and states *"Verification: `docs/superpowers/verification/2026-07-11-tabbed-entity-detail/dod.md`"*, but that folder contains only `gate10-api-definition-rendered.png` — **no `dod.md`, no `gate-findings.yaml`**.
- **Violates:** CLAUDE.md §Definition of Done — *"each slice maintains a DoD ledger at `docs/superpowers/verification/<date>-<topic>/dod.md` … Completion claims MUST cite the ledger path — the `.claude/hooks/dod-check.js` stop hook blocks claims that don't"* and *"Alongside `dod.md`, each slice keeps `gate-findings.yaml`"*.
- **Impact:** The "done" claim is unsubstantiated at the pre-merge gate: gates 1/2/5/7/8/9 have no citable ledger row, and gate-10 (the *primary* evidence for a frontend slice per the design's Testing Strategy, lines 82-88/113) is proven only for the API Definition surface. The design's gate-10 script (plan lines 705-712) requires Service Overview (endpoints table renders), Service/Application Dependencies (api-surface tables render, no blank-page), the Application Edit-dialog heavy-re-render path, and the `?tab=bogus` normalization — none of which have committed evidence. Because ADR-0084 blank-paging is exactly the class jsdom recovers from silently, the missing Service/Application browser screenshots are the highest-value absent artifacts.
- **Fix:** Add `dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`) with a row per gate (3 & 6 = N/A-with-reason, 4/11 = PR CI, others green + command/output) and `gate-findings.yaml` (copy the template) logging this review's findings; capture the remaining gate-10 screenshots (Service Overview + Dependencies, Application Overview + Dependencies with Edit dialog open, `?tab=bogus`→Overview) into the same folder. **Test that would catch it:** the `.claude/hooks/dod-check.js` stop hook — run it against a completion claim for this slice; it should block until the ledger exists.

---

### Should-fix issues

**S1 — No page-level test proves the spec render mounts on the API Definition tab.**
`web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx:44-50` asserts the three tab *roles* exist and that Dependencies shows relationships, but nothing deep-links `?tab=definition` and asserts `ApiSpecSection` renders there. The single most important behavioral claim of the slice — "spec moved onto the Definition tab" (design line 20, plan Task 2) — is verified only by the generic primitive test, not on the real page. A regression that dropped `<DetailTabs.Tab id="definition">` from `ApiDetailPage.tsx:398-400` would keep every existing page test green.
- **Violates:** ADR-0097 unit-coverage expectation (design line 110: *"Definition only on API"* named as a to-test assertion).
- **Fix:** add to `ApiDetailPage.test.tsx` a test `renders the spec section on the Definition tab` → `renderPage("?tab=definition")` then assert the Definition panel's spec UI is present (e.g. `screen.getByRole("tab", { name: "Definition" })` is `aria-selected="true"` and a spec-section marker such as the "Spec document" heading / "No spec attached." empty state renders).

**S2 — Section reordering inside the Dependencies tab is a silent behavioral change.**
On both `ServiceDetailPage.tsx:666-681` and `ApplicationDetailPage` (diff lines 525-540) the moved sections now render **API surface → mini-graph → …**, whereas the pre-tab pages rendered **mini-graph → API surface → …** (old Service order visible at diff lines 686-690). The design table (line 36) does list "API surface · mini-graph" in that order, so this is defensible — but it is an unremarked visual reorder that the design never calls out as intentional and no test pins. Confirm it is deliberate; if so, one line in the ADR-0114 consequences or the CHECKLIST note would remove the ambiguity.
- **Fix:** none required in code if intended; otherwise restore prior order. Optionally pin order with a test asserting the api-surface heading precedes the mini-graph region in DOM order.

---

### Nits (cap 5)

1. `web/src/components/application/tabs/detail-tabs.tsx:62` — `aria-label={ariaLabel}` on `TabList` is fed the entity `displayName` from every call site (e.g. `ApiDetailPage.tsx:312`). Screen readers announce the tablist as the entity name rather than its purpose; a static label ("Detail sections") reads better and the entity name is already the page `<h2>`.
2. `web/src/features/catalog/components/ApiSpecSection.tsx:71` — Definition empty state reads "No spec attached."; the design (line 80) specified "No specification attached" + attach link. Cosmetic wording only (the attach affordance exists at line 50, permission-gated).
3. `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx:806-812` — the Application "no Definition tab" test does not also assert the successor block stays on Overview, though the plan's Task 4 test (plan line 527) named "successor stays on Overview"; the shipped assertion is narrower than planned.
4. `detail-tabs.tsx:61` — clicking back to the first tab from a non-clean URL leaves `?tab=overview` rather than restoring the clean URL. Consistent with react-aria/controlled semantics and not a spec violation ("absent is left clean" is about initial load), but worth a one-line comment so a future reader doesn't mistake it for a normalization miss.
5. ADR-0114 body (`ADR-0114-...md:14`) says the spec chunk is "~2.8 MB"; harmless copy carried from PR #69 — fine, just noting it is a repeated figure not re-measured for this slice.

---

### Missing tests (acceptance criteria with no test)

- **API Definition-tab render** (design line 20/110) — see S1: `ApiDetailPage.test.tsx` → `renders the spec section on the Definition tab` deep-linking `?tab=definition`.
- **Application successor stays on Overview** (plan Task 4, line 527) — `ApplicationDetailPage.test.tsx` → assert the "Successor" block is present on the default Overview render (currently only tab-role presence is asserted).
- **ADR-0084 heavy-re-render survival** (design lines 82-88) — not unit-testable (jsdom recovers silently, per the design); acceptable to leave to gate-10, but that browser evidence is the B1 gap. No new unit test expected here.

---

### What looks good

1. `web/src/components/application/tabs/detail-tabs.tsx:35-58` — the /simplify fixes landed cleanly: all hooks (`useSearchParams`, shared `setTab` `useCallback`, normalization `useEffect`) run **before** the `if (tabs.length === 0) return null` guard (line 58), no `useMemo`, no speculative `paramName`, and the effect + `onSelectionChange` share the single `setTab`. Effect deps are derived scalars (`raw, isKnown, defaultId, setTab`), sidestepping the recomputed-`ids`-array instability.
2. ADR-0084 hazard fully mitigated — every table that moved into a tab panel keeps exactly one `isRowHeader`: `ApiSurfaceSection.tsx:30,98`, `DerivedDependenciesSection.tsx:35`, `RelationshipsSection.tsx:99,115`, plus the Service endpoints table on Overview (`ServiceDetailPage.tsx` diff line 616), with a unit guard `getAllByRole("rowheader").length).toBeGreaterThan(0)` at `ServiceDetailPage.test.tsx` (diff line 877).
3. Header-above-tabs honored on all three pages, including the action-bearing Application header (`ApplicationDetailPage.tsx:76-96`: `LifecycleMenu` + `AssignTeamPicker` + Edit stay in `CardHeader`, tabs in `CardContent`) — matches design lines 43/78.
4. Lazy Definition preserved — `ApiSpecSection.tsx:12` `const SpecRender = lazy(...)`, mounted only inside the `definition` `TabPanel`; react-aria mounts only the active panel and `shouldForceMount` is deliberately unused (design line 74 / ADR-0114 consequences), so the ~2.8 MB Scalar chunk stays off Overview.
5. Documentation is complete and internally consistent: ADR-0114 created, README summary row (diff line 38), both category lists and two keyword-index entries updated (diff lines 47/56/64/72), decision-log row appended (diff line 80), and CHECKLIST S-04 flipped with a substantive summary — the only inconsistency is the ledger path in that same CHECKLIST line (B1).
