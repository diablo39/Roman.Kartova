# TD-004 — gate-9 live verification (2026-09-10)

**Fix:** cursor binds `sortBy` (`sf` discriminator); a mid-pagination sort-field switch → 400
`cursor-sort-field-mismatch`. ADR-0095 amended 2026-09-10.

**Scope (per owner):** verification limited to gate 3 (full test suite) + gate 9 (live API).
Gates 5–8, 10 (PR/CI) not run for this TD cleanup.

## Gate 3 — full test suite

`dotnet test Kartova.slnx -c Debug` — all 15 assemblies green.

- Changed-code unit assemblies: `Kartova.SharedKernel.Tests` 146/146, `Kartova.SharedKernel.AspNetCore.Tests` 101/101.
- `Kartova.Catalog.IntegrationTests` first reported 467/467 **failed** — assembly-init
  `System.TimeoutException` on the Docker named-pipe (Testcontainers container start under
  saturation); the documented full-suite flake, zero tests ran. Isolated re-run:
  **467/467 passed** in 1m31s.

## Gate 9 — live API (rebuilt `kartova/api:dev` image, real KeyCloak JWT + real Postgres/RLS)

Token: password grant, client `kartova-api`, user `admin@orga.kartova.local`.
Endpoint: `GET /api/v1/catalog/applications`. Driver: `scratchpad/gate9.py`.

```
[1] GET ?sortBy=displayName&sortOrder=asc&limit=2 -> 200
    items: ['A App 015', 'A App 041']
    nextCursor: eyJzIjoiQSBBcHAgMDQxIiwiaSI6ImJh...   (now carries sf=displayName)

[2] replay cursor under SAME sortBy=displayName -> 200 (expect 200)
    items: ['A App 067', 'A App 093']              # normal paging unaffected

[3] replay cursor under DIFFERENT sortBy=createdAt -> 400 (expect 400)
    type:   https://kartova.io/problems/cursor-sort-field-mismatch
    detail: Cursor was issued for sortBy=displayName but request uses sortBy=createdAt.
    expectedField: displayName   actualField: createdAt

RESULT: PASS
```

- [3] is the TD-004 defect: `displayName` and `createdAt` differ, so pre-fix the `createdAt`
  keyset predicate ran against the `displayName` boundary value and silently skipped/repeated
  rows. Now rejected 400 before any query.
- [2] is the control proving the guard does not break same-field paging (matching `sf` passes,
  page advances).
```
