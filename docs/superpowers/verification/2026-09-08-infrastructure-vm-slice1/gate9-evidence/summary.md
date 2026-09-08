# Gate 9 — Visual / API verification (observe running system)

Observed the running stack (final images: `kartova/{migrator,api,web}:dev`, HEAD c56bb3d) — api:8080, keycloak:8180, web:4173, Postgres — with real OIDC auth (admin@orga.kartova.local, tenant OrgA `11111111-…`).

## Running-system DB (migration applied)
`catalog_infrastructure` present with all columns (`type smallint`, `system_id` nullable, `attributes jsonb`), **FORCED RLS `tenant_isolation` policy (cmd=ALL)**, and **GIN `jsonb_path_ops` index** `ix_catalog_infrastructure_attributes` + shared btree indexes. (See `\d` output captured during the run.)

## Live API (real auth + DB + RLS)
- `GET /api/v1/catalog/infrastructure/vms` → 200, rehydrated VM attributes (`vms-list.json`).
- `GET /api/v1/catalog/infrastructure` → 200, **shared columns only, `type:"virtualMachine"`, NO attributes block** — kind-agnostic generic tier holds live (`generic-list.json`).
- `POST /api/v1/catalog/infrastructure/vms` with `vcpu`/`memoryGb` as JSON **strings** → **201**; GET-by-id roundtrip shows `vcpu=4` (int), `memoryGb=16`, both IPs preserved — the SPA string-wire path works live (ASP.NET `AllowReadingFromString`).
- `GET .../vms?powerState=running` → 2 running VMs, suspended/stopped excluded (`filter-powerState.txt`) — `@>` containment filter works live.
- **EXPLAIN** (`explain-gin.txt`): seqscan on the ~7-row seed table (correct at tiny scale); with `enable_seqscan=off` → **Bitmap Index Scan on `ix_catalog_infrastructure_attributes`** with `Index Cond: attributes @> '{"powerState":"running"}'` — confirms the GIN index is usable for the `@>` query at scale.

Aside (not slice-specific): a malformed-UTF-8 request body returned 500 rather than 400 — a framework decoder-fallback edge on garbage bytes, applies to any endpoint; noted, not a slice defect.

## UI (real browser, claude-in-chrome; Playwright MCP was down this session)
- `ui-01-vm-list.jpg` — Virtual Machines list: nav (Software + Infrastructure → Virtual Machines active, Brokers disabled, All Objects last), permission-gated "Register VM" button, FW3 exact-match filter placeholders, power-state badges, **multi-IP "+1"** rendering, sortable Name/Created only. Renders (no blank page).
- `ui-02-register-vm-modal.jpg` — Register VM modal open: **list table behind stays rendered (dimmed), NO blank page** — the isRowHeader modal-open footgun (CLAUDE.md) is clear in a real browser (the check jsdom cannot make). All form fields render.
- `ui-03-all-objects-list.jpg` — All Objects generic list: shared columns (Name · Type orange badge · Team · System "—" · Created), no attributes block — kind-agnostic tier confirmed visually.
- Console: no errors/exceptions on the rendered screens.

**Verdict: PASS.** Both the API two-tier behavior and the UI (incl. the modal-open isRowHeader check) verified on the running system.

Note: a throwaway VM `gate9-verify-vm` was created via the live POST and left in the dev DB (no delete endpoint in slice 1).
