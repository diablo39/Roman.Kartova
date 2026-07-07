# ADR-0112: API Spec Artifacts Stored as `text` in Postgres, Not MinIO

**Status:** Accepted (2026-07-07)
**Date:** 2026-07-07
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform / Domain Model
**Related:** ADR-0004 (narrows — MinIO default for blob storage), ADR-0034 (distinct — OpenAPI *auto-render*, not storage), ADR-0111 (amended alongside this ADR — API is the owning entity), ADR-0001 (PostgreSQL), ADR-0012 (RLS)

## Context

The API family (`Api.Style ∈ {Rest, Grpc, GraphQL, AsyncApi}`) needs to store the API's spec document — OpenAPI for `Rest`/`Grpc`/`GraphQL`, AsyncAPI for `AsyncApi`, WSDL for a possible future SOAP style. These documents are text (JSON/YAML/XML), typically **20–50 KB**, with a **~1 MB tail** for large/composed specs.

ADR-0004 already establishes S3-compatible blob storage (MinIO default) as the platform's general-purpose blob mechanism, used for logo uploads, doc assets, and GDPR export bundles. The question is whether spec documents should go through that same path, or be stored directly in PostgreSQL alongside the owning API row.

## Decision

Store spec documents as **`text`** in a dedicated, RLS-scoped **`catalog_api_specs`** table, one row per API (1:1, `api_id` unique), written **transactionally** with the owning `Api` aggregate. A `media_type` column tags the document's serialization (e.g. `application/json`, `application/yaml`, `text/xml`); the semantic format (OpenAPI vs. AsyncAPI vs. WSDL) is **not** duplicated here — it derives from `Api.Style`.

**Not** MinIO/S3 for this data class.

## Rationale

- **Free tenant isolation.** A Postgres table under the same RLS policy as every other catalog table needs no separate per-tenant prefixing or bucket-policy scheme (contrast ADR-0004's `tenants/{tenant-id}/` prefix convention).
- **Transactional integrity.** The spec is written in the same transaction as the owning `Api` row and its audit entry (`api.spec.updated`) — no orphaned blobs, no eventual-consistency window between "API says it has a spec" and "the spec actually exists," which a two-system (Postgres + MinIO) write would require compensating logic to guarantee.
- **Uniform ops.** One backup/restore/DR story (Postgres), no second storage system to provision, monitor, and reason about for what is, at this size, a small amount of text.
- **TOAST handles the tail.** PostgreSQL's TOAST mechanism transparently out-of-lines and compresses `text` values above ~2 KB, so the 20–50 KB typical case and ~1 MB tail are both handled without schema changes or performance cliffs at this scale.
- **Size profile doesn't justify MinIO yet.** ADR-0004's blob storage earns its keep for larger or binary payloads (images, ZIP export bundles); a same-order-of-magnitude-as-a-large-JSON-row document does not need a separate object store.

## Alternatives Considered

- **MinIO (per ADR-0004's default path), API row holds a reference.** Rejected for now: adds a second system to keep consistent with the API row (orphan/dangling-reference risk on the write path, extra round-trip on read), for no benefit at 20–50 KB / ~1 MB tail. Revisit if the data shape changes (see Consequences).
- **Postgres `bytea` instead of `text`.** Rejected: specs are structured text (JSON/YAML/XML), not binary; `text` is the natural type and keeps values human-inspectable via ordinary SQL tooling.
- **Store the spec inline as a column on `Api` itself, no dedicated table.** Rejected: keeps the (potentially large) spec payload on every read of the API row, and pre-empts trivial extension to per-version storage (E-21) without a table split.

## Consequences

**Positive**

- Zero additional infrastructure for spec storage — reuses the existing Postgres/RLS/transaction machinery.
- Spec writes are atomic with the owning API row and its audit trail.
- TOAST absorbs the current size profile (typical + tail) with no special-casing.

**Negative / trade-offs**

- `catalog_api_specs` grows with API count × document size; at large tenant scale (ADR-0074) this is more data in the primary OLTP store than a blob-store approach would leave there.
- No native CDN/signed-URL delivery path (irrelevant today — specs are fetched via authenticated API, not served as static assets).

**Neutral / revisit trigger**

- **Revisit → MinIO** if **E-21 (version history)** makes this **many-versions × ~1 MB × many-APIs**, and table bloat starts to bite (large-table vacuum/backup cost, TOAST pressure). At that point, migrate to the ADR-0004 MinIO path with the Postgres row holding a reference, mirroring how ADR-0020 was scoped for cold archival.
- This ADR **narrows** ADR-0004 for the API-spec-document data class only; ADR-0004 remains the default for all other blob storage (logos, doc assets, GDPR exports).
- Distinct from **ADR-0034** (OpenAPI is *auto-generated and self-rendered* for Kartova's own API) — this ADR is about *storing user-supplied* spec documents for cataloged third-party/internal APIs, not about Kartova's own OpenAPI generation.

## References

- ADR-0004 (S3-compatible blob storage, MinIO default) — narrowed by this ADR for API spec documents.
- ADR-0034 (OpenAPI auto-generated and self-rendered) — distinct concern.
- ADR-0111 (API is a first-class entity) — amended alongside this ADR (2026-07-07) to reflect the unified API entity and `AsyncApi` style value.
- ADR-0001 (PostgreSQL), ADR-0012 (Row-Level Security).
- Implementing slice: `docs/superpowers/specs/2026-07-07-catalog-async-api-spec-storage-design.md` (E-02.F-03.S-02).
