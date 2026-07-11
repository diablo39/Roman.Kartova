# ADR-0114 — Tabbed entity-detail layout

Status: Accepted
Date: 2026-07-11

## Context

Catalog entity-detail pages (API, Service, Application) had grown into long single-scroll cards: description + metadata + dependency mini-graph + API surface + relationships + (API only) an inline spec render. The OpenAPI/AsyncAPI spec render (Scalar, ~2.8 MB lazy chunk) sat at the bottom of the API page. Backstage and Compass both surface a component's API definition on a dedicated surface, not inline on an overview.

## Decision

Introduce a shared `DetailTabs` primitive (react-aria `Tabs`, ADR-0094) and split each detail page into tabs:
- Application / Service: Overview · Dependencies.
- API: Overview · Dependencies · Definition (spec render).

Conventions: active tab in `?tab=` (default `overview`, `replace` writes, invalid deep-link normalized to default); per-entity tab sets reflect real content only (no empty tabs); react-aria mounts only the active panel, so the Definition Scalar chunk loads lazily on open. The entity header (name, badges, action buttons) stays above the tab bar.

## Consequences

- Tab switch is a heavy re-render: every in-panel react-aria `<Table>` must keep exactly one `isRowHeader` column, verified in a real browser (ADR-0084) — jsdom recovers silently.
- Switching a tab re-mounts its panel (re-runs cheap, React-Query-cached section queries). `shouldForceMount` deliberately not used.
- Future per-entity surfaces (Documentation E-11.F-01, Deployments E-02.F-05, Settings) slot in as new tabs per feature.
- Amends the detail-page UI convention; supersedes nothing.
