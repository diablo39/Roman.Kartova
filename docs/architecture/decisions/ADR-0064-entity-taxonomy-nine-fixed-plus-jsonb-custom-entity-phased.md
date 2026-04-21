# ADR-0064: Entity Taxonomy — Nine Fixed Types with JSONB Custom Attributes, Custom Entity Type Phased

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0012 (RLS), ADR-0013 (ES index), ADR-0054 (deep repository scan), ADR-0061 (four-tier pricing), ADR-0065 (hybrid org structure), ADR-0067 (relationship origin tracking), ADR-0068 (fixed relationship vocabulary), ADR-0069 (required minimum fields), ADR-0070 (scorecard configurability), ADR-0071 (five-level maturity model), ADR-0072 (tag taxonomy), ADR-0073 (entity lifecycle states)

## Context

The original PRD §3.1 defined nine entity types (Application, Service, API-Sync, API-Async, Infrastructure, Message Broker, Queue/Topic, Environment, Deployment), each with a fixed, opinionated schema. This opinionated taxonomy is a core Kartova differentiator against Backstage's fully generic `kind`/`spec` model — it enables type-specific rich UI, type-specific auto-import mapping (ADR-0054), and consistent analytics (Risk Score, DX Score, Maturity Model per ADRs 0071/0019/0018).

Two real pressures challenge a purely-fixed taxonomy:

1. **Tenant-specific metadata** — enterprise customers want to track cost centers, compliance framework references, data classifications, backup tiers, business owners, and similar attributes that vary by organization. Hard-coding these as columns forces platform schema changes for each customer need.

2. **Edge-case entity types** — some real-world components do not fit cleanly into any of the nine types:
   - **Batch jobs / cron jobs** — not Service (not always-on), not Deployment (it *is* the job, not a specific deployment)
   - **ML training pipelines / DAG workflows** — orchestration entities that don't have endpoints or continuous runtime
   - **Workflow definitions** (Airflow DAG, Temporal workflow) — orchestration primitives distinct from Services

Most other "edge cases" (mobile apps, ML serving endpoints, webhook receivers, edge workers) actually fit well into Application/Service with metadata. Only a small but real fraction is genuinely unclassifiable.

Solo-developer constraint requires phasing: implementation of a 10th type adds ~15-20% effort over pure-9-types; can be deferred until core-catalog patterns are established.

## Decision

Kartova implements entity taxonomy in two phases:

### Phase 1: Nine Fixed Types with JSONB Custom Attributes (MVP — Phase 0 / Phase 1 early)

Retain the nine fixed types from PRD §3.1 as first-class entities, each with their own specialized schema and full platform feature support. Each fixed-type table adds a `custom_attributes JSONB` column for tenant-specific metadata:

| Entity | Type | Auto-import | Rich UI | Analytics (DX/Risk/Maturity) | custom_attributes |
|--------|------|-------------|---------|------------------------------|-------------------|
| Application | Fixed | Yes | Yes | Yes | Yes |
| Service | Fixed | Yes | Yes | Yes | Yes |
| API (Sync) | Fixed | Yes | Yes | Yes | Yes |
| API (Async) | Fixed | Yes | Yes | Yes | Yes |
| Infrastructure | Fixed | Yes | Yes | Yes | Yes |
| Message Broker | Fixed | Yes | Yes | Yes | Yes |
| Queue/Topic | Fixed | Yes | Yes | Yes | Yes |
| Environment | Fixed | Yes | Yes | N/A | Yes |
| Deployment | Fixed | Yes | Yes | N/A | Yes |

**custom_attributes rules (MVP):**
- Free-form JSONB — no tenant-defined schema validation
- Arbitrary keys with string, number, boolean, array, or object values
- Displayed in entity UI as generic key/value list at the bottom of detail page
- Editable at Pro tier and above (ADR-0061) — Free/Starter read-only
- Indexed in Elasticsearch (ADR-0013) as flattened fields under prefix `custom.`
- Searchable, filterable, and tag-like queryable
- Excluded from built-in Risk/DX/Maturity calculations
- Can be referenced by tenant-defined scorecard rules (ADR-0070) — e.g., "every Service must have `custom.cost_center`"

**Storage pattern:**
```sql
ALTER TABLE applications ADD COLUMN custom_attributes JSONB NOT NULL DEFAULT '{}';
ALTER TABLE services       ADD COLUMN custom_attributes JSONB NOT NULL DEFAULT '{}';
-- ...same for all 9 fixed-type tables

CREATE INDEX idx_applications_custom ON applications USING GIN (custom_attributes);
-- Per-tenant expression indexes on hot keys can be added later
```

### Phase 2: Tenth Custom Entity Type (Deferred — Phase 1 late / Phase 2)

After the MVP catalog patterns are established, add a tenth **Custom Entity** type for genuinely unclassifiable components. Custom Entity is a generic component with a flexible schema and **opt-out** of type-specific platform features:

| Capability | Custom Entity |
|------------|---------------|
| Name, owner, description (ADR-0069 required minimums) | ✓ |
| Lifecycle states (ADR-0073) | ✓ |
| Tags (ADR-0072) | ✓ |
| custom_attributes JSONB | ✓ |
| `subtype_label` — free-text descriptor (e.g., "Batch Job", "ML Pipeline") | ✓ |
| Relationships with all entity types (ADR-0067/0068) | ✓ |
| Embedded in dependency graphs | ✓ |
| Documentation hub | ✓ |
| Basic scorecards (ADR-0070) | ✓ — tenant-defined rules only |
| Auto-import (ADR-0054) | ✗ — always manual creation |
| Rich type-specific UI | ✗ — generic form |
| DX Score (ADR-0019) | ✗ — opt-out |
| Risk Score (ADR-0018) | Limited — ownership + staleness only |
| Maturity Model (ADR-0071) | Limited — L1/L2 only, higher levels require type-specific signals |

**Storage:**
```sql
CREATE TABLE custom_entities (
  id UUID PRIMARY KEY,
  tenant_id UUID NOT NULL,
  name TEXT NOT NULL,
  description TEXT,
  owner_team_id UUID NOT NULL,
  subtype_label TEXT,
  lifecycle_state TEXT NOT NULL DEFAULT 'active',
  custom_attributes JSONB NOT NULL DEFAULT '{}',
  tags TEXT[] NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE custom_entities ENABLE ROW LEVEL SECURITY;
ALTER TABLE custom_entities FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON custom_entities
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
CREATE INDEX idx_custom_entities_tenant_name ON custom_entities (tenant_id, name);
```

**UI decision tree (when user clicks "Register New Entity"):**
```
1. UI presents the 9 fixed types first (with icons and short descriptions)
2. "Doesn't fit any of these?" link at the bottom opens Custom Entity form
3. Custom Entity form requires: name, owner, description, subtype_label, tags
```

## Rationale

**Phased approach chosen because:**
- **95% of use cases** covered by nine fixed types plus JSONB custom_attributes — MVP does not require Custom Entity to be useful
- **Pattern observation** in Phase 1 reveals which edge-case types actually emerge among dogfooding and design-partner usage (ADR-0079); Custom Entity design can then reflect real tenant needs rather than speculation
- **Solo-developer effort** concentrated on the harder, more valuable parts (fixed-type rich UI, auto-import, analytics) before adding the simpler 10th generic type
- **Reduced abuse risk** — launching without Custom Entity avoids the "dumping ground" failure mode during the critical early-adoption window when tenants learn the taxonomy

**JSONB custom_attributes chosen because:**
- **Tenant extensibility without schema changes** — enterprise metadata (cost_center, compliance_framework, data_classification, backup_tier) varies per org; hard-coding new columns for each customer would require per-tenant migrations
- **PostgreSQL JSONB is production-grade** — GIN indexes, path operators, rich query support; well-supported by EF Core
- **Elasticsearch flattened indexing** (ADR-0013) already handles dynamic JSON fields — search and filter work out of the box
- **Promotion path** — if a JSONB key becomes common across many tenants (e.g., `cost_center` used by 50%+), it can be promoted to a regular column later with a data migration
- **Complementary, not competitive** — custom_attributes solve "need more fields on existing type," Custom Entity solves "need a different type entirely"; both are needed long-term

**Nine fixed types (retained) chosen because:**
- **Opinionated differentiator** against Backstage's generic model — Kartova claims to "know how to model services" and the type system encodes that knowledge
- **Auto-import mapping** (ADR-0054) relies on file patterns → specific types (Dockerfile → Application infra signal, OpenAPI spec → API Sync, AsyncAPI → API Async, etc.)
- **Type-specific analytics** — DX Score questions ("does this API have a spec?", "does this Service have a health check?") only make sense per type
- **Consistent cross-tenant benchmarking** — org-wide dashboards need comparable entities, not tenant-defined taxonomy

## Alternatives Considered

- **Pure nine fixed types (no JSONB, no Custom Entity) — PRD original:** Rigid; cannot accommodate enterprise metadata or edge-case types; forces data quality tradeoffs. Rejected.
- **Generic Component only (single type with `type` discriminator + JSONB):** Abandons opinionated differentiation; Kartova becomes "Backstage with nicer UI"; auto-import becomes generic pattern-matching with lower precision; analytics lose type context. Rejected.
- **User-extensible entity types (tenant-defined schemas):** Per-tenant migrations, per-tenant UI generation, per-tenant auto-import rules; explodes operational surface for a solo developer. Rejected.
- **Backstage-style kind/spec with plugin system:** Depends on plugin architecture (ADR-0038), deferred to v2.0+. Cannot be MVP. Rejected.
- **Nine fixed types + JSONB, no Custom Entity ever:** Covers 95% of cases but leaves batch jobs, ML pipelines, and workflow definitions without a clean home. Acceptable for MVP, insufficient long-term. Rejected in favor of phased inclusion.
- **Nine fixed types + Custom Entity, no JSONB on fixed types:** Handles true edge cases but forces tenants to use Custom Entity every time they need one extra field on a Service — poor UX, loses type benefits. Rejected.
- **All three (fixed + JSONB + Custom Entity) shipped simultaneously in MVP:** Adds 15-20% effort during the critical early milestones when other foundational work (auth, RLS, scan engine) dominates. Rejected in favor of phasing.

## Consequences

**Positive:**
- MVP ships with nine rich, type-specialized entities plus trivial-to-implement custom_attributes for tenant metadata — high value at low incremental cost
- Real tenant usage (design partners per ADR-0079) informs the Custom Entity design before it is built
- Clear separation of concerns: `custom_attributes` for tenant metadata, Custom Entity (future) for new entity kinds
- Elasticsearch and Risk/DX/Maturity analytics remain clean for the nine fixed types
- Promotion path from JSONB key to regular column is a standard data-evolution pattern
- Documentation and pricing-page messaging are simpler at launch — nine types + "custom fields"; 10th type added when we have a concrete story for it

**Negative / Trade-offs:**
- During Phase 1, edge-case entities (batch jobs, ML pipelines) must be forced into the closest fixed type (usually Service or Application) — imperfect data quality for those tenants until Phase 2
- Two delivery milestones for taxonomy increase the risk of scope drift (Phase 2 could slip); must be committed on the roadmap
- `custom_attributes` without schema validation can drift into inconsistent usage across a tenant — mitigated by tenant scorecard rules (ADR-0070) that enforce expected keys
- UI must handle the "doesn't fit any type" case gracefully even before Custom Entity exists (Phase 1 response: "Contact support" or force-choose closest)
- Analytics limitations on Custom Entity (opt-out of DX Score, limited Maturity) must be documented and communicated; tenants must understand why Custom Entity is "lesser" for scoring
- Auto-import cannot categorize anything as Custom Entity — always requires manual user decision, which is a UX cost

**Neutral:**
- JSONB columns add minor storage overhead per row — negligible
- Custom Entity when added requires matching scorecard engine, relationship engine, and graph visualizer changes — incremental rather than novel work
- Pricing-page messaging updates for Phase 2: Custom Entity available on all tiers; advanced analytics-on-custom-entity could become a Pro feature if needed

## Implementation Notes

**Phase 0–1 (MVP) — custom_attributes on all fixed types:**

1. Add `custom_attributes JSONB NOT NULL DEFAULT '{}'` to all nine fixed-type tables (migration)
2. GIN index per table on `custom_attributes`
3. API: include `customAttributes` field in all entity CRUD endpoints (create/read/update)
4. UI: generic key/value list at bottom of entity detail page; edit button enabled for Pro+ tier (ADR-0061)
5. Elasticsearch mapping: dynamic template on `custom.*` path with `type: keyword` default
6. Scorecard rule engine (ADR-0070) supports JSONPath expressions like `custom_attributes.cost_center != null`
7. Search API supports `filter[custom.cost_center]=CC-123` query param

**Phase 1 late / Phase 2 — Custom Entity:**

1. Create `custom_entities` table with schema above
2. API: `/api/v1/custom-entities` CRUD endpoints
3. UI: generic registration form with `subtype_label` free-text field and `custom_attributes` editor
4. Relationship engine: allow Custom Entity on both sides of any relationship type (ADR-0068 vocabulary)
5. Dependency graph: Custom Entity nodes rendered with neutral icon and `subtype_label` as hover tooltip
6. Analytics opt-outs coded explicitly in Risk/DX/Maturity scorers (skip custom_entities table)
7. Basic scorecard rules (tenant-defined) work on custom_entities like any other type
8. Migration path: "Convert Custom Entity to [fixed type]" admin action — moves row between tables, preserves relationships

**PRD §3.1 amendment (MVP):** Add a note after the nine-entity table:
> All entity types support a free-form `custom_attributes` JSONB field for tenant-specific metadata (cost center, compliance framework, data classification, etc.). A tenth generic "Custom Entity" type is planned for Phase 2 to accommodate edge-case components that don't fit the nine fixed types.

**PRD §3.1 amendment (Phase 2):** Add Custom Entity row to the entity table:
> | **Custom Entity** | Generic component for edge cases not fitting the nine fixed types | Name, description, owner, subtype_label, custom_attributes, tags |

## References

- PRD §3.1 (Tracked Entity Types — to be amended)
- Phase 1 Epic E-02 (Entity Registry), Feature E-02.F-01 (Application), E-02.F-02 (Service), E-02.F-03 (API), E-02.F-04 (Infrastructure & Broker), E-02.F-05 (Environment & Deployment)
- PostgreSQL JSONB: https://www.postgresql.org/docs/current/datatype-json.html
- PostgreSQL GIN index on JSONB: https://www.postgresql.org/docs/current/gin-intro.html
- Elasticsearch flattened field type: https://www.elastic.co/guide/en/elasticsearch/reference/current/flattened.html
