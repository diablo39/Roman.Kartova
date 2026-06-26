# Deep PR Review — feat/catalog-service-ui-surface (E-02.F-02.S-02)

**Status:** OPEN (pre-merge gate)
**Diff:** `4eab9ff..HEAD`, code under `web/src` (17 commits)
**Spec:** docs/superpowers/specs/2026-06-20-catalog-service-ui-surface-design.md
**Plan:** docs/superpowers/plans/2026-06-20-catalog-service-ui-surface.md
**ADRs cross-referenced:** ADR-0095 (cursor pagination), ADR-0094 (Untitled UI stack), ADR-0103 (required owning team), ADR-0097 (testing tiers)

### Overview
The slice ships the frontend surface for the catalog `Service` entity: a zod schema + protocol/health vocab, three TanStack Query hooks, a controlled endpoints editor, a register dialog, a sortable services table, a `/catalog/services` list page, a read-only `/catalog/services/:id` detail page, and router + sidebar wiring. It is frontend-only — the endpoints, `catalog.services.register` permission, and `service.registered` audit already shipped on master in S-01 with real-seam integration tests. Health renders as a read-only badge (always `unknown`); consumers/dependencies are explicitly deferred to E-04.

### Blocking-class issues
**None.** Every spec §3 decision is implemented and traceable; all 11 plan tasks are complete with acceptance criteria honored; the full frontend Vitest suite is green (472/472), the production build and the web Docker image build are green, and CI's snapshot-fallback codegen path was validated to produce the Service types. No DoD gate is unsatisfiable.

### Should-fix issues

**1. Backend OpenAPI metadata gap for `ListServices` (S-01 follow-up).**
- **Evidence:** `web/src/features/catalog/api/services.ts` — `ListServicesQuery["sortBy"]`/`["sortOrder"]`/`limit` resolve to bare `string`; the `String(params.limit ?? 50)` cast compensates. Compare `ListApplications` which generates `sortBy: "createdAt" | "displayName"` and `limit?: number`.
- **Impact:** The generated client doesn't pin the Services sort vocabulary, so a backend casing drift wouldn't fail `tsc`; `SortField` is re-declared locally as the interim guard. No runtime impact (backend parses string params + allowlist-validates).
- **Fix:** Backend — annotate the `/catalog/services` list endpoint params with the same enum/integer metadata `ListApplications` uses (the `CursorListQueryParameterTransformer` + typed `sortBy`). Out of scope for this frontend slice; file against the Catalog module. The frontend `String()` cast + local `SortField` can then be removed.

**2. CI does not run the frontend Vitest suite — a broken test merged undetected.**
- **Evidence:** `.github/workflows/ci.yml` frontend job runs only `npm ci → codegen → typecheck → build` (no `npm test`). This slice's gate-3 run found `web/src/shared/auth/__tests__/usePermissions.test.tsx` failing on master (S-01 added `catalog.services.register` to `KartovaPermissions` but didn't update the OrgAdmin "all permissions" fixture); fixed here in `367e34b`.
- **Impact:** Frontend unit/component regressions can merge silently. The only reason this was caught is the local DoD gate-3 full-suite run.
- **Fix:** Add a `test` step (or a dedicated `frontend-test` job) running `npm test` to `.github/workflows/ci.yml`. Separate slice/PR (CI infra), but it is the root cause of a real defect that reached master.

**3. Detail page fetches up to 200 teams to resolve one team name.**
- **Evidence:** `web/src/features/catalog/pages/ServiceDetailPage.tsx` — `useTeamsList({ ..., limit: 200 })` to map one `svc.teamId → displayName` for a single link label.
- **Impact:** A deep-link to a service detail (cold team cache) fetches the full team list for one label. Bounded (200) and cached via the shared `useTeamsList` query key, so cheap in practice, but the right fix is server-side.
- **Fix:** Embed `teamName` in `ServiceResponse` (backend), or add a single-team `useTeam(id)` hook. Until then the current bounded+cached fetch is acceptable.

**4. Frontend lint is red (5 errors) and not gated.**
- **Evidence:** `npm run lint` → `react-hooks/set-state-in-effect` in `RegisterServiceDialog.tsx` (reset-on-close effect) plus 4 pre-existing identical instances (`RegisterApplicationDialog.tsx`, `ChangeMemberRoleDialog.tsx`, `ChangeRoleDialog.tsx`, `UserSearchCombobox.tsx`). Lint is not in CI or `ci-local.sh`.
- **Impact:** The "lint clean" plan constraint is unmet repo-wide; the rule isn't enforced anywhere. This slice's instance mirrors the `RegisterApplicationDialog` reset-on-close pattern verbatim.
- **Fix:** Repo-wide lint-debt cleanup as a separate task (migrate the reset effects or scope the rule), and add `npm run lint` to CI once green. Not blocking this slice (matches the established precedent).

### Nits (capped at 5)
1. `web/src/features/catalog/pages/ServiceDetailPage.tsx` — "Created" uses `toLocaleString()` (date+time) while the tables use `toLocaleDateString()` (date only). Minor display inconsistency; brief-specified.
2. `web/src/features/catalog/schemas/registerService.ts` — `PROTOCOL_LABEL` (UI display vocab) lives in the form-schema module; arguably belongs beside `health.ts`. Acceptable since `PROTOCOLS` feeds the `z.enum` in the same file.
3. `web/src/features/catalog/components/EndpointsEditor.tsx` — endpoint rows keyed by array index; safe here because each `Input` is fully controlled (`value={row.url}`), so no stale-value bug on middle-remove.

### Missing tests
- **ServicesListPage error card branch.** `ServicesListPage.tsx` renders a "Failed to load services" card + Reset on `list.isError`; `ServicesListPage.test.tsx` covers heading + register-gating only, not the error branch. The `CatalogListPage` test also omits this, so it's consistent — add `list.isError → error card renders` for completeness (low priority).
- **E2E.** No Playwright coverage of the create→list→view flow. Deferred by design — the checked-in E2E suite (E-01.F-02.S-03) is Not Started; manual Playwright MCP smoke stands in. Not a gap against this slice's scope.
- Schema/component/hook/page coverage is otherwise complete: schema happy+negatives incl. boundary (≥50, url max-length), hook GET/POST/invalidation, editor add/remove/cap/update round-trip, dialog submit/validation/ProblemDetails-vs-toast/reset-on-close, table rows/links/health/count/empty/sort, detail loaded/empty/skeleton/not-found.

### What looks good
1. **Compile-time wire-contract guards.** `health.ts` types its maps as `Record<Health, …>` and `registerService.ts` pins `PROTOCOLS` with `as const satisfies readonly ProtocolValue[]` — both derive from the generated client, so any enum casing drift fails `tsc` rather than silently producing wrong wire values. (ADR-0095/codegen-driven contract.)
2. **Endpoint error-index alignment.** `RegisterServiceDialog.tsx` pre-allocates `rowErrors` to `endpoints.length` and walks by original index while dropping empty-URL rows from the payload — the trickiest invariant in the slice, implemented correctly and covered by a per-row-error test.
3. **Faithful sibling mirroring.** `services.ts`/`ServicesTable`/`ServicesListPage`/`ServiceDetailPage` mirror the Application equivalents (query-key factory, `useCursorList`/`useListUrlState`/`<DataTable>` per ADR-0095, perm-gated register button), keeping the catalog surface consistent.
4. **Snapshot-fallback codegen verified.** The committed `openapi-snapshot.json` carries the Service types, and codegen-with-API-down was confirmed to regenerate a typechecking client — so CI's frontend job (which has no live API) will pass.
5. **Default list sort honored.** `ServicesListPage` defaults to `displayName`/`desc` per the stated convention, distinct from `CatalogListPage`'s `createdAt`/`desc`.
