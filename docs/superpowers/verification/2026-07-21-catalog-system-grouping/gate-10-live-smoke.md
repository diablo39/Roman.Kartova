# Gate 10 — Visual/API live smoke (E-03.F-03.S-01)

**Date:** 2026-07-21 · **Arm:** A (ship arm, PR #78) · **Stack:** `docker compose up` from `.worktrees/armA` (real Postgres 18/RLS + KeyCloak 26.1 JWT; migrator applied `AddSystems`).
**Auth:** password-grant token for `admin@orga.kartova.local` (OrgAdmin), client `kartova-api`, realm `kartova`.

| # | Request | Expected | Actual |
|---|---------|----------|--------|
| 1 | `GET /api/v1/catalog/systems?limit=5` | 200 CursorPage | **200** `{"items":[],"nextCursor":null,"prevCursor":null}` |
| 2 | `POST /api/v1/catalog/systems` `{displayName:"Payments Platform", description, teamId}` | 201 + SystemResponse | **201** — full response incl. `tenantId`, `createdByUserId`, `createdAt`, `createdBy` init prop (mirrors ApiResponse) |
| 3 | `GET /api/v1/catalog/systems/{id}` | 200 round-trip | **200** identical body |
| 4 | `POST /systems` empty `displayName` | 400 | **400** |
| 5 | `POST /relationships` Application→System `partOf` | 201 | **201** |
| 6 | `GET /relationships?entityKind=application&entityId=…&direction=outgoing` | edge visible (option A) | **`[('partOf','system')]`** — visible, no special-casing |
| 7 | `GET /api/v1/catalog/api-surface?entityKind=system&entityId=…` | 400 (critique fix) | **400** |
| 8 | `POST /relationships` System→System `partOf` | 400 (disallowed pair) | **400** |

**Result: PASS.** Every endpoint + both pre-implementation-critique fixes behave correctly on the live stack against the real security/DB seam. `createdBy` resolves to `null` (the seeded user isn't projected into `IUserDirectory` in DevSeed — identical behavior to Api/Application on this stack, not slice-specific). No drift/unknown-unknowns surfaced (brand-new entity, no legacy rows). Registered System id `eb5034af-…` left in the dev DB (throwaway compose volume).
