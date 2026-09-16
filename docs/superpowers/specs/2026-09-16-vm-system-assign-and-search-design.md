# Design — VM System-side assign + server-side VM name search (TD-009 + TD-007)

**Date:** 2026-09-16
**Story:** E-02.F-04.S-01 follow-ups (VM linking). Closes TD-007, TD-009.
**Slice:** Branch A of the TD-009/007/008 cleanup. Branch B (TD-008 vitest flake) is a separate test-infra chore — not covered here.
**Type:** vertical slice (backend filter + FE dialog). ~small, well under the 400-line target.

## Problem

Two UX-parity gaps left open by the 2026-09-16 VM-linking slice (#91 / TD-005/006):

- **TD-007** — `GET /catalog/infrastructure/vms` has no `displayNameContains` param, so the DeployOnVm / entity-search VM typeahead shows the first N VMs by name regardless of typed text (`useEntitySearch` infra branch, `relationships.ts:131-143`, sends no search term). Poor at scale.
- **TD-009** — `AddSystemMemberDialog` (System detail → Members → "Assign component") offers only Application / Service radios, so a steward viewing a System cannot add a VM to it in place. Infra membership is only settable from the VM-side *Assign* dialog, though the backend (`useSetComponentSystem` → `PUT /catalog/infrastructure/{id}/system`) and the read/remove paths (TD-006) already support it.

TD-007 is a dependency of TD-009: without it the new System-side VM picker wouldn't narrow on typed text.

## Approach

Mirror the existing `ListServices` name-filter pattern for VMs, then flip on the `infrastructure` radio that the FE plumbing already supports.

### Backend (TD-007)
Add `DisplayNameContains` to the VM list, mirroring `ListServicesQuery`/`ListServicesHandler` **exactly**:
- `ListVmsQuery` — new `string? DisplayNameContains = null` optional param (last position, default keeps all 3 existing constructions valid — see Impact Analysis).
- `ListVmsHandler.Handle` — `EF.Functions.ILike(x.DisplayName, pattern, "\\")` predicate; escape `%`/`_`/`\` in the term (copy `ListServicesHandler`'s escaping verbatim). Add `displayNameContains` to `BuildFilterMap` so a mid-pagination change trips `CursorFilterMismatchException` (same as Services).
- `ListVmsAsync` delegate — `[FromQuery] string? displayNameContains`, `string.IsNullOrWhiteSpace(...) ? null : .Trim()` (same guard the other delegates use), pass to the query. No `CatalogModule` registration edit (minimal-API auto-binds; method-group ref unchanged).
- Regenerate the OpenAPI snapshot + typed client (predev/prebuild does this; commit the diff).

**Semantics note (registry):** VM `displayNameContains` is a **substring `ILike`** over the `DisplayName` **column** — distinct from the existing `os`/`region`/`hostname`/`ipAddress` filters, which are **exact jsonb-containment** matches over `VmAttributes`. Same substring semantics as Applications/Services/APIs/Systems name search.

### Frontend (TD-007 + TD-009)
- `relationships.ts` `useEntitySearch` — delete the special-case infra branch (lines 131-143); route `infrastructure` through the shared `q` object like the other kinds (drop the string-`limit` workaround once the client is regenerated — confirm the regenerated VM `limit` param type; if still `string`, keep a minimal branch that passes `displayNameContains` + string `limit`). Remove the stale "ListVms has no displayNameContains" comment.
- `AddSystemMemberDialog.tsx` — add `{ value: "infrastructure", label: "Infrastructure" }` to `KINDS`. Combobox/mutation already generic over `ComponentKind` (= `PartOfSourceKind`, includes `infrastructure`). Placeholder `Search infrastructures…` reads oddly → override to `Search VMs…` for the infra kind (small copy tweak).

## Out of scope / deferred
- TD-008 (vitest flake) — Branch B.
- Broker registration (E-02.F-04.S-02).
- Generic-infra (non-VM) search — only VMs are a real InfrastructureType today; `useEntitySearch("infrastructure")` targets `/vms` deliberately.
- `has-spec`-style presence filters, VM attribute substring search — unchanged.

## Testing strategy (per docs/TESTING-STRATEGY.md)

Wiring slice touching an HTTP query param + DB predicate + cursor f-map → **real-seam** gate-3 artifacts required.

- **Integration (real Postgres/RLS, `KartovaApiFixtureBase`):** `GET /catalog/infrastructure/vms?displayNameContains=` — ≥1 happy (narrows to matching VM, case-insensitive) + ≥1 negative (no match → empty page; `%`/`_` in term treated literally, not as wildcards). Mirror `ListServicesPaginationTests`/`ListApisPaginationTests`.
- **Unit:** extend `ListVmsHandlerFilterTests` — `BuildFilterMap` includes `displayNameContains` when set, omits when null.
- **Frontend (vitest):** `AddSystemMemberDialog` renders the Infrastructure radio; selecting it + choosing a VM calls `useSetComponentSystem` with `componentKind: "infrastructure"`. `useEntitySearch("infrastructure", q)` sends `displayNameContains`.
- **Container build (gate 4):** N/A-with-reason candidate — diff touches no Dockerfile/COPY/restore surface (`.csproj`/`Directory.Packages.props`/`nuget.config` untouched). Confirm at gate 4.
- **E2E-impact (gate 9 trigger):** the System detail Members flow is traversed by no current `e2e/` spec (system-graph specs cover graph, not the assign dialog) — confirm during gate 9; if a spec asserts the dialog's kind set, update + run it.

## DoD
Ten always-blocking gates per CLAUDE.md — ledger at `docs/superpowers/verification/2026-09-16-vm-system-assign-and-search/dod.md`. Gate 9 (visual): drive the System-side assign of a VM on the running stack + confirm the picker narrows on typed text.
