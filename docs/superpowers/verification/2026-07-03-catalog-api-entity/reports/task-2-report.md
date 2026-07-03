# Task 2 Report — Permission `catalog.apis.register` + role map + 5-sync

Branch: `feat/catalog-api-entity`
Commit: `f5e0aed` — `feat(catalog): add catalog.apis.register permission (Member+OrgAdmin) + 5-sync`

## Files changed (exactly the 6 specified in the plan)

1. `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` — added `CatalogApisRegister = "catalog.apis.register"` const (after `CatalogServicesRegister`) and added it to the `All` initializer array (same position).
2. `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` — added `KartovaPermissions.CatalogApisRegister` to both the `[KartovaRoles.Member]` and `[KartovaRoles.OrgAdmin]` arrays, immediately after `CatalogServicesRegister`.
3. `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` — appended two tests before the closing brace: `Member_and_OrgAdmin_can_register_apis_but_Viewer_cannot` and `CatalogApisRegister_is_in_the_All_set`.
4. `web/src/shared/auth/permissions.snapshot.json` — added `"catalog.apis.register",` after `"catalog.services.register",`.
5. `web/src/shared/auth/permissions.ts` — added `CatalogApisRegister: "catalog.apis.register",` after the `CatalogServicesRegister` line.
6. `web/src/shared/auth/__tests__/usePermissions.test.tsx` — added `"catalog.apis.register",` to the OrgAdmin mock's `permissions` array in the "returns OrgAdmin set with all permissions" test, after `"catalog.services.register",`.

## TDD evidence

### RED (C#) — Step 1 + 2

After adding the two new tests referencing the not-yet-existing `KartovaPermissions.CatalogApisRegister`:

```
> dotnet test tests/Kartova.SharedKernel.Tests -v q
...KartovaRolePermissionsTests.cs(156,103): error CS0117: 'KartovaPermissions' does not contain a definition for 'CatalogApisRegister'
...KartovaRolePermissionsTests.cs(157,105): error CS0117: 'KartovaPermissions' does not contain a definition for 'CatalogApisRegister'
...KartovaRolePermissionsTests.cs(158,104): error CS0117: 'KartovaPermissions' does not contain a definition for 'CatalogApisRegister'
...KartovaRolePermissionsTests.cs(163,74): error CS0117: 'KartovaPermissions' does not contain a definition for 'CatalogApisRegister'
```
Confirmed compile failure exactly as expected (Step 2).

Also ran `Kartova.ArchitectureTests` after adding the C# const/map but *before* syncing the frontend snapshot, to confirm the arch-test catches drift:

```
> dotnet test tests/Kartova.ArchitectureTests -v q
Failed!  - Failed:     1, Passed:    68, Skipped:     0, Total:    69
```
Failure detail: `Ts_snapshot_equals_csharp_KartovaPermissions_All` — `CollectionAssert.AreEquivalent Expected:<20>. Actual:<19>.` (C# `All` now has 20 entries, snapshot still has 19) — exactly the intended drift-guard failure mode.

### GREEN (C#) — Step 3, 4, 6

After adding the const + `All` entry (Step 3) and the Member/OrgAdmin role-map grants (Step 4), then syncing the frontend snapshot (Step 5):

```
> dotnet test tests/Kartova.SharedKernel.Tests -v q
Passed!  - Failed:     0, Passed:   125, Skipped:     0, Total:   125, Duration: 3 s

> dotnet test tests/Kartova.ArchitectureTests -v q
Passed!  - Failed:     0, Passed:    69, Skipped:     0, Total:    69, Duration: 3 s
```

Note: `dotnet test tests/Kartova.SharedKernel.Tests tests/Kartova.ArchitectureTests -v q` (passing both project paths to a single `dotnet test` invocation) fails with `MSB1008: Only one project can be specified` on this SDK version — ran the two projects as separate invocations instead; both are green.

### GREEN (frontend) — Step 7

```
> npx vitest run usePermissions permissions
 Test Files  1 passed (1)
      Tests  7 passed (7)
```

Only `usePermissions.test.tsx` matched the filter (no separate test file exists for `permissions.ts` in isolation — the drift-guard in `permissions.ts` runs at module-import time and is exercised implicitly whenever `usePermissions.test.tsx` imports the permissions module). Since the suite imported cleanly and all 7 tests (including the OrgAdmin-set assertion) passed, both the OrgAdmin-loop test and the drift-guard are confirmed green.

Did not run `npm ci` or touch `node_modules` per instructions; used the already-installed `vitest` via `npx vitest run` directly (equivalent to `npm run test -- ...` for this repo's config).

## Self-review

- `git diff --stat` before commit showed exactly the 6 files listed in the plan, no stray changes.
- Full `git diff` reviewed line-by-line — every insertion matches the plan's exact strings/positions (Member and OrgAdmin arrays both received the grant at the same relative position as `CatalogServicesRegister`; frontend snapshot/const/test mock all inserted at the matching position).
- No CRLF introduced (all edits made via the Edit tool against existing LF files, no raw-write path used).
- Commit created with the exact staged file list and exact message from the plan's Step 8.

## Concerns

- None. All 5 mirrors (C# const, C# `All`, C# role map ×2 roles, TS snapshot, TS const, frontend test mock) are in sync and both test suites (SharedKernel + Architecture, plus the frontend permission tests) are green.
