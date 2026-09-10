# Gate 9 — Visual / API verification (running system)

**Date:** 2026-09-10 · **Stack:** `docker compose up -d --build` from `chore/tech-debt-td-001-002-003` (api :8080, web :4173, real Postgres + KeyCloak). Auth: `admin@orga.kartova.local` via KeyCloak password grant (`kartova-api` client) for API, OIDC login for the browser.

## TD-001 — null-safe keyset (Provider sort) — PASS

Seeded 5 VMs in one team: 3 with `provider=null`, 2 with `provider` = `zzz-alpha` / `zzz-bravo`. Paginated `GET /api/v1/catalog/infrastructure/vms?sortBy=provider&limit=2`, walking every page:

- **asc** → `zzz-alpha, zzz-bravo, <null>, <null>, <null>` — non-nulls ascending first, **NULLS LAST**; all 5 rows returned across the page boundary that falls inside the null block, **no truncation**.
- **desc** → `<null>, <null>, <null>, zzz-bravo, zzz-alpha` — **NULLS FIRST**, then non-nulls descending; all 5 returned.

Live cursor paging on real Postgres crosses the NULL boundary correctly (the exact case the old `?? ""` workaround and the pre-fix predicate mishandled).

**Visual:** VM list at `/catalog/infrastructure/vms?sortBy=provider&sortOrder=asc` renders (react-aria table, no blank-page) with the Provider column sorted ascending — see `gate9-vm-provider-sort-asc.jpg`. The `g9-*` null-provider rows render `—` and sort to the end.

## TD-002 — shared concurrency-token capture (412 currentVersion) — PASS

On one seeded VM (`GET` ETag `"ChEAAA=="`):
- `PUT` with correct `If-Match: "ChEAAA=="` → **200**, new ETag `"EBEAAA=="`.
- `PUT` again with the now-stale `If-Match: "ChEAAA=="` → **412** `concurrency-conflict` with body `"currentVersion":"EBEAAA=="` — the hint captured by the shared `ConcurrencyTokenCapture` (EF metadata) and emitted by `ConcurrencyConflictExceptionHandler`, matching the current version. End-to-end through the live VM edit path.

## TD-003 — FE unmapped-400 → toast — not live-triggerable (covered by tests)

The fix is defense-in-depth: it only changes behavior for a 400 whose error key maps to no registered form field. The backend now returns keys matching field paths, so this path can't be forced through the live API without a synthetic backend fault. Covered instead by unit + dialog tests (`problemDetails.test.ts` mixed-key case; `EditVmDialog.test.tsx` unmapped-only + mixed → toast). No runtime surface to drive; noted rather than faked.

## Result

Gate 9 PASS for the two items with a live runtime surface (TD-001, TD-002); TD-003 has no live-triggerable surface and is verified by tests. Evidence: this file + `gate9-vm-provider-sort-asc.jpg`.
