# Deep Review — VM System-side assign + server-side VM name search (TD-007 + TD-009)

**Target:** branch `feat/catalog-vm-system-assign-search` vs `master` (HEAD `c594817`)
**Spec:** `docs/superpowers/specs/2026-09-16-vm-system-assign-and-search-design.md`
**Plan:** `docs/superpowers/plans/2026-09-16-vm-system-assign-and-search-plan.md` (gitignored scratch)
**ADRs cross-referenced:** ADR-0095 (cursor lists), ADR-0107 (list filters / field-addition trigger), ADR-0111 (relationships / PartOf edges), ADR-0094 (UI stack)
**Reviewer:** in-session (deep-review skill). Runs after the gate-2/7 review pass whose findings are already applied in `c594817`.

## Overview
Two tightly-scoped follow-ups. TD-007 adds a `displayNameContains` substring filter to the VM list, mirroring `ListServices` verbatim; TD-009 flips on the `infrastructure` radio in the System-side assign dialog (backend + FE plumbing already generic). Production diff is ~30 lines across 4 files; the rest is tests + docs. The slice is faithful to its spec and the cited ADRs. All should-fix findings raised by the five gate-2/7 reviewers are resolved in the second commit.

## Blocking
None.

## Should-fix
None outstanding. The three real should-fixes from the review pass are fixed in `c594817`:
- `relationships.test.tsx:110-115` — VM search test now asserts `displayNameContains` is passed (was a `objectContaining` blind spot) + stale "backend gap" comment removed.
- `relationships.ts:131-136` — comment corrected to the real cause (`CursorListQueryParameterTransformer` not applied to `/infrastructure/vms`), not the backend binding.
- `list-filter-registry.md:36` — `displayNameContains` scoped to the entity-search typeahead; VM list-screen FilterBar facet marked **explicitly deferred** per the ADR-0107 field-addition trigger (was overstated as a built facet).

## Nits (triaged — not applied)
- `AddSystemMemberDialog.tsx:23-25` — `searchPlaceholder` helper is untested (cosmetic string; no functional risk). Skip.
- `KINDS` (`AddSystemMemberDialog.tsx:17`) is a hand-maintained subset of `ComponentKind` with no compile-time exhaustiveness pin — a future 4th source kind would compile while silently missing from the dialog. Pre-existing, not introduced here. Skip.
- `ListVmsHandler.cs:48-52` ILIKE block is byte-identical to `ListServicesHandler.cs:45-49` (and the Apis/Systems handlers). Established convention; extraction not worth it. Skip.

## Missing tests
None critical. Covered adequately:
- Real-Postgres ILIKE case-insensitivity + wildcard escaping (`%` **and** `_`) — `InfrastructureVmEndpointsTests`.
- `BuildFilterMap` single-key + all-keys (7) — `ListVmsHandlerFilterTests`.
- FE: radio search-routing + VM-selection mutation payload + real query-shape assertion.
- Cursor×filter mismatch + multi-page-with-filter are covered by shared, separately-tested machinery (ADR-0095 `CursorFilterMismatchException` proven on Applications); not duplicated per handler. Cross-tenant-with-filter-active is a low-probability gap (RLS is applied uniformly and isolation is tested elsewhere) — accepted.

## What looks good
- `ListVmsHandler.cs:48-52` — ILIKE + `LikeEscaping.EscapeLike` + `"\\"` escape char is a verbatim mirror of the established pattern; escape order (`\` before `%`/`_`) verified in `LikeEscaping.cs:12-16`.
- `ListVmsHandler.cs:86,105-106` — the new dimension is added to **both** the `BuildFilterMap` null-guard and the emit, so cursor-stability detection (ADR-0095) stays honest; the common "predicate-without-f-map" footgun is avoided.
- `ListVmsHandler.cs:48` — predicate applied to `source` **before** `ToCursorPagedAsync`, so a name-filtered-out row never becomes a cursor boundary (same invariant as the sibling filters).
- `ListVmsQuery.cs:41-42` — new param appended last with `= null`, keeping all existing constructions valid (LSP-confirmed: 9 refs, only the delegate supplies the arg).
- `AddSystemMemberDialog.tsx:17` — TD-009 is purely additive: `ComponentKind`/`useSetComponentSystem` already carried `infrastructure` (`systems.ts:137` → `PUT /catalog/infrastructure/{id}/system`), so the radio is correct by construction.

## DoD note
Gate 4 (container) N/A — no Dockerfile/COPY/restore surface. Gate 9 (visual) pending — drive the System-side VM assign on the running stack.
