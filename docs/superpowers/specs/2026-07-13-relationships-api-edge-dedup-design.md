# Design — De-dup API provide/consume edges from the Relationships list

**Story:** E-04.F-01.S-05 · **Issue:** [#71](https://github.com/diablo39/Roman.Kartova/issues/71)
**Date:** 2026-07-13 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** ADR-0111 (edge model — *unchanged*, this doc is the required design note) · ADR-0114 (tabbed detail — *amended*: tab-ordering convention)

---

## Problem

On Application and Service detail pages the **Dependencies** tab renders two sections that both surface the same edges:

1. `ApiSurfaceSection` — **Provides** / **Consumes** tables, rich columns (name · style · version · Spec badge · derived `via …` provenance).
2. `RelationshipsSection` — **Outgoing** / **Incoming** tables, which re-list the same stored `providesApiFor` / `consumesApiFrom` edges as plain `Type · Entity · Origin · Added-by` rows.

Grouping both under one Dependencies tab (E-11.F-02.S-04 tabbed layout) made the duplication obvious. The API-surface section is the richer, canonical view of provide/consume edges; the Relationships list should stop repeating them.

The **API detail page** must be unaffected: it renders `RelationshipsSection variant="incoming-only"` with **no** API-surface section, so those edges are the only way to see an API's providers/consumers there.

## Goal

On entities that render the API surface (Application, Service), exclude `providesApiFor` / `consumesApiFrom` edges from the Relationships list. Everywhere else (the API detail page), leave them. No change to relationship creation.

## Non-goals

- No change to the Add-outgoing dialog — it still offers `Provides API for` / `Consumes API from`; the created edge surfaces in the API-surface section above. Relocating the creation entry point is a separate follow-up.
- No merged/unified relationships table (rejected — would drop the API surface's richer columns and mix read-only derived rows with editable manual rows).
- No new "APIs" tab (Option 2, deferred): API surface stays a section inside the Dependencies tab.
- No change to the standalone `/graph` explorer, mini-graph, derived-dependencies, or the `GET /catalog/api-surface` endpoint.

## Approach

Exclusion lives in the **backend query**, opt-in via a flag the full-variant callers set. Filtering rows client-side would desync the cursor pager (a page would render fewer than `limit` rows while the server still reports a `Next` cursor). Server-side exclusion keeps pagination truthful.

### Backend — `Kartova.Catalog`

- `ListRelationshipsForEntityQuery` (Application) gains `bool ExcludeApiEdges`.
- `ListRelationshipsForEntityHandler` (Infrastructure): when `ExcludeApiEdges` is set, apply **before** pagination:
  ```csharp
  source = source.Where(r =>
      r.Type != RelationshipType.ProvidesApiFor &&
      r.Type != RelationshipType.ConsumesApiFrom);
  ```
- `CatalogEndpointDelegates.ListRelationshipsAsync`: add `[FromQuery] bool? excludeApiEdges` (default `false`) → forwarded into the query. Default-false ⇒ endpoint is backward-compatible; existing callers/tests unaffected.
- Regenerate the OpenAPI spec → `web/openapi-snapshot.json` (predev/prebuild) and the generated client.

### Frontend — `web`

- `RelationshipsListParams` gains `excludeApiEdges?: boolean`; `useRelationshipsList` forwards it into the query string (as `"true"` / omitted).
- `RelationshipsSection`: derive `const excludeApiEdges = variant === "full"` and pass it to both list hooks.
  - `variant="full"` — Application/Service pages ⇒ exclude.
  - `variant="incoming-only"` — API detail page ⇒ `false` ⇒ providers/consumers still shown.

### ADR-0114 amendment — tab-ordering convention

Document (no code change — current pages already comply): tab order is **Overview → Dependencies → entity-specific content tabs → cross-cutting tabs**. Dependencies is fixed at position 2 across every entity kind for predictability. Current sets already satisfy it: Application/Service = `Overview · Dependencies`; API = `Overview · Dependencies · Definition`.

## Data flow

```
App/Service Dependencies tab
  → RelationshipsSection variant="full"
    → GET /catalog/relationships?entityKind&entityId&direction=outgoing&excludeApiEdges=true
      → Outgoing list = dependsOn / instanceOf only
  ApiSurfaceSection  → GET /catalog/api-surface  (Provides/Consumes — the sole home of API edges)

API Dependencies tab
  → RelationshipsSection variant="incoming-only"  (excludeApiEdges=false)
    → Incoming list still shows providesApiFor / consumesApiFrom  (unchanged)
```

## Edge cases

- **Incoming on App/Service** never contains provide/consume edges (their target is always `api`), so the flag is a no-op there — harmless and consistent to pass.
- **Derived** API entries (e.g. a Service deriving a provides edge via its `instance-of` Application) live only in the API surface and are computed, not stored `Relationship` rows — the exclusion (which filters stored edge *types*) never touches them.
- **Pager correctness** preserved: exclusion is applied pre-pagination, so page counts and the `Next`/`Prev` cursors stay truthful.
- `excludeApiEdges` absent/false ⇒ identical to today (regression-safe default).

## User journeys

**J1 — Service provides an API (core dup).** Service `payments-api-svc` has `provides-api-for → Payments REST v2` and `depends-on → ledger-svc`. Dependencies tab: API surface **Provides** shows `Payments REST v2` (REST · v2 · Spec · Direct). Relationships **Outgoing** — *before:* `Depends on → ledger-svc` **and** `Provides API for → Payments REST v2` (dup); *after:* `Depends on → ledger-svc` only. Net: the API appears exactly once.

**J2 — Application consumes APIs.** App `checkout-web` has two `consumes-api-from` edges, no other outgoing. API surface **Consumes** lists both. Relationships **Outgoing** — *before:* two `Consumes API from` rows; *after:* "No outgoing relationships." + the Add-outgoing button.

**J3 — API detail page (must NOT change).** API `Payments REST v2`, provided by `payments-api-svc`, consumed by `checkout-web`. Dependencies tab (`incoming-only`, no API surface): Relationships **Incoming** shows `Provides API for ← payments-api-svc` and `Consumes API from ← checkout-web`. Before == after. Regression guard against a naive global type filter.

**J4 — Add a provides edge still works.** On `notifications-svc`, Add-outgoing still offers `Provides API for`; user links `Notify AsyncAPI v1`. On refresh the edge appears in API surface **Provides** (above), not in the Outgoing table. Creation entry point unchanged.

**J5 — Derived edge unaffected.** `ledger-svc` is `instance-of ledger-app`, which `provides-api-for Ledger REST v1`. API surface **Provides** shows `Ledger REST v1` (origin `Derived · via ledger-app`). Relationships **Outgoing** shows `Instance of → ledger-app` (kept — a real stored edge, not an API edge). Before == after.

**J6 — Mixed page, pagination honesty.** `hub-svc` has 25 outgoing edges (15 dependsOn, 10 providesApiFor), page size 20. *Server-side exclude:* page 1 = up to 20 dependsOn rows with `Next` iff more remain; page 2 = remaining dependsOn. *(Rejected client filter:* page 1 renders ~12 rows but still shows `Next` → desynced.) Confirms why exclusion is server-side.

## Testing strategy

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). This slice touches an HTTP query param + a DB query (a wiring slice) ⇒ gate-5 real-seam integration tests are deliverables.

- **Integration (real seam — `KartovaApiFixtureBase`, real Postgres/RLS + real JWT):** extend `ListRelationshipsTests`:
  - `excludeApiEdges=true` omits `providesApiFor`/`consumesApiFrom`, retains `dependsOn`/`instanceOf` (happy).
  - default (`excludeApiEdges` absent) still returns all types (negative / regression-safe default).
  - mixed set spanning a page boundary is not short-counted and the `Next` cursor is honest (J6).
- **Frontend (Vitest + Testing Library):**
  - `RelationshipsSection.test`: `variant="full"` issues the request with `excludeApiEdges` set; `variant="incoming-only"` does not.
  - `ApplicationDetailPage.test` / `ServiceDetailPage.test`: no duplicate provide/consume row across API-surface + Relationships.
- **Container build (gate-4):** no Dockerfile/`COPY` change ⇒ standard `images` job suffices; N/A for a bespoke test.
- **Gate-6 (mutation):** conditional — the `Where` predicate is Application/Infrastructure logic; small surface ⇒ run it, target ≥80%, document survivors.
- **Gate-10 (visual):** browser-verify the App/Service Dependencies tab shows each API once and the API detail page still lists providers/consumers. Flag *pending user verification* if the Playwright MCP is unavailable this session.

## Slice size

~30 lines production business code (backend query flag + handler `Where` + endpoint param; frontend param plumb + one derived flag). Well under the ~400-line target. Single vertical slice.

## DoD gates

Ten always-blocking gates per CLAUDE.md. Gate-5 real-seam required (wiring slice). Gate-6 conditional-blocking (Application/Infra logic touched) ⇒ run. Gate-10 UI change ⇒ browser-verify (or flag pending). Ledger at `docs/superpowers/verification/2026-07-13-relationships-api-edge-dedup/dod.md`.
