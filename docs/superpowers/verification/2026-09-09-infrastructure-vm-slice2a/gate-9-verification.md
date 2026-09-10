# Gate 9 — Visual / API verification (Infrastructure/VM slice 2a)

**Date:** 2026-09-09 · **Method:** live stack (`docker compose up`, built slice images) + claude-in-chrome, in-SPA (ADR-0084).
**Stack:** web `http://localhost:4173` (built `kartova/web:dev`), API `:8080`, KeyCloak `:8180`, Postgres `:5432`. Migrator container ran the slice migration and exited clean (provider column + 6 partial indexes applied). Login `admin@orga.kartova.local` (OrgAdmin).

> Screenshot-to-disk (`Page.captureScreenshot` save) repeatedly timed out in this environment (flaky CDP); findings below are from live in-session observation of the rendered screens.

## Verified working

- **VM list** (`/catalog/infrastructure/vms`) renders with the full column set: Name (sort ↑ default), **Provider (sortable)**, Power state (badge, sortable), OS (sortable), vCPU (sortable), Memory (sortable), Hostname (sortable), IP addresses, Region (sortable), Team. All new JSONB sort headers present with sort affordance.
- **Provider column** present on the VM list (shows `—` for null-provider rows, which the seed data currently has).
- **isRowHeader invariant** holds — exactly one `rowheader` (the Name column) per row; opening the react-aria **Register VM overlay did NOT blank-page** the screen (the CLAUDE.md react-aria `<Table>` gotcha is guarded).
- **Multi-IP render** — `gate9-verify-vm` shows `10.9.9.9` / `10.9.9.10` one per line.
- **Register VM dialog** opens as a react-aria overlay; **provider field present** in the create form (optional, "AWS, Azure, on-prem…"); IP-addresses InputTags works (typed IP + Enter → chip added).
- Auth + deep-link to the VM list work (OIDC round-trip through KeyCloak, in-SPA navigation).

## Finding (real Critical — fixed) — VM creation via the SPA is broken

**Observed:** In the Register VM dialog, filled every required field, selected **Team = "Demo Team"** (visibly selected in the dropdown), added an IP, clicked **Register Virtual Machine** → the form showed red **"Team is required"** directly under the (selected) Team dropdown and did NOT submit. VM cannot be created through the SPA.

**Root cause:** `RegisterVmDialog.tsx` wired the team `<select>` through RHF `FormField`/`useController` on a native `<select>` inside a react-aria `Form` — a controlled-select interaction that does not register the selected value, so `registerVmSchema.teamId` validation always fails. The sibling `RegisterApplicationDialog.tsx` documents this exact issue (its lines ~29-30) and works around it by managing teamId in a separate `useState` and validating in the submit handler.

**Scope:** pre-existing since slice 1 (RegisterVmDialog shipped then); this slice touched RegisterVmDialog (added the provider field), and gate-7's type/review lens flagged it for gate-9 confirmation. The existing VMs in the list (`gate9-verify-vm`, `app-vm-03`, `sql-vm-02`, `web-vm-01`) were created via API/seed/integration paths, not the SPA dialog.

**Fix:** applied the RegisterApplicationDialog `useState`-for-teamId pattern to RegisterVmDialog (schema `.omit({teamId:true})` for the RHF fields, plain controlled `<select>`, teamId validated in submit) + a regression test that selects a team and asserts the create POST fires with `teamId`. See commit and `task-gate9-teamid-fix-report.md`.

## API verification (via integration tests, gate 3/7/8)

Live PUT/DELETE/GET with If-Match (412/428) and the JSONB-sort `EXPLAIN` partial-index-use proofs are covered by the real-seam `InfrastructureVmWriteTests` / `InfrastructureVmSortTests` (real Postgres/RLS + JWT; `DbCommandInterceptor`-captured EXPLAIN). Gate 9's own live API drive is therefore N/A-by-coverage for those; the gate's unique value here was the exploratory UI pass that surfaced the teamId-create bug automated tests structurally missed.

## Fix verified live (post-rebuild)

Rebuilt the web image with the teamId fix (`ebf7303`), restarted the `web` container, re-ran the create flow in-browser: filled the Register VM form, selected **Demo Team**, provider **AWS**, submitted → green toast **"Virtual machine registered"** and a new row **`gate9-create-ok`** appeared in the list with **Provider = AWS** (Running, Ubuntu 24.04, 10.9.9.77, eu-west-1, Demo Team). VM creation via the SPA now works, and this is also the end-to-end confirmation of **provider create + column display** (first non-null provider rendered). Gate 9 PASS.
