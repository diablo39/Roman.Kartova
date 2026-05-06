# Playwright Smoke Evidence — Sorting + Cursor Pagination (ADR-0095)

**Date:** 2026-05-04 (executed 2026-05-05)  
**Branch:** feat/sorting-pagination  
**Slice:** ADR-0095 cursor pagination on Applications list  

## Stack used

- Keycloak (Docker): `http://localhost:8180`  
- API (local dotnet run): `http://localhost:5021` (Development, connected to Dockerised Postgres)  
- Vite dev server: `http://localhost:5173`  
- Seed: 120 applications inserted for Org A (tenant `11111111-1111-1111-1111-111111111111`) via local migrator `--seed=dev`  
- Auth: `admin@orga.kartova.local` / `dev_pass` (Keycloak realm seed)

## Playwright sequence executed

1. Navigate to `http://localhost:5173/` → redirected to Keycloak login.
2. Fill username `admin@orga.kartova.local` and password `dev_pass`, click Sign In.
3. Redirected to `/catalog` — waited 3 s for data to load.
4. **Screenshot 01** — default sort (`createdAt:desc`).
5. Click **Name** column header → URL becomes `?sortBy=name&sortOrder=asc`, data reloads.
6. **Screenshot 02** — name sort ascending (first row: A App 015).
7. Click **Next** button → cursor advances to page 2.
8. **Screenshot 03** — page 2 of name-asc sort (first row: K App 005; Prev now enabled).
9. Click **Prev** button → cursor returns to page 1.
10. **Screenshot 04** — page 1 restored (last row: J App 110; Prev disabled, Next enabled).
11. Checked console — zero React errors, zero unhandled rejections. One stale 400 from an earlier bad-port redirect attempt (port 5174 not whitelisted in Keycloak); irrelevant to the actual session.

## Screenshots

| File | What it shows |
|------|--------------|
| `01-default-sort.png` | Catalog on load: `createdAt:desc` (A App 119 first, newest seed), Prev disabled, Next enabled, 50 results |
| `02-name-sort-asc.png` | After clicking Name header: `name:asc` (A App 015 first), Name column has up-arrow indicator |
| `03-page-2.png` | After clicking Next: page 2 of name-asc (K App 005 first), both Prev and Next enabled |
| `04-page-1-back.png` | After clicking Prev: page 1 restored (J App 110 last row visible), Prev disabled again |

## Console verdict

**CLEAN.** No error-level messages from the application session. The single logged error is a `400 Bad Request` from an initial navigation attempt to an unwhitelisted redirect URI (`localhost:5174`) — this occurred before the correct Vite port (5173) was established and is not part of the tested scenario.
