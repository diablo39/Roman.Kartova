# ADR-0004: S3-Compatible Blob Storage with MinIO Default

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform / Storage
**Related:** ADR-0001 (PostgreSQL), ADR-0015 (GDPR cascade deletion), ADR-0020 (cold archival), ADR-0022 (cloud-agnostic K8s), ADR-0026 (proprietary product)

## Context

Kartova needs blob storage for several cross-cutting concerns from Phase 0 onward:

- Logo uploads (organization logos and status-page branding).
- Documentation assets (images, diagrams) extracted from imported docs.
- GDPR data export artifacts (data portability ZIP bundles).
- Potentially scan-result caches and cold archival data.

ADR-0015 references blob storage as a target for GDPR cascade deletion alongside PostgreSQL and Elasticsearch. Scale is modest (a few GB per tenant), but the capability is required from Phase 0. The choice must respect ADR-0022 (cloud-agnostic Kubernetes deployment) and ADR-0026 (fully proprietary — no open-source core concerns) — i.e. we can use open-source infrastructure as long as we do not modify and redistribute it in ways that trigger copyleft obligations.

## Decision

Use **S3-compatible blob storage** accessed through an **`IBlobStorage` abstraction** in the .NET API (either [FluentStorage](https://github.com/robinrodricks/FluentStorage) or a custom interface with implementations per backend). Default production deployment uses **MinIO** deployed via the **MinIO Operator** on the same Kubernetes cluster as the platform.

**Bucket layout:** a shared bucket `kartova` with per-tenant prefixes `tenants/{tenant-id}/` — simplifies GDPR cascade deletion (single prefixed `DeleteObjects`) and minimizes operational overhead.

Enterprise / on-prem / managed-cloud deployments can swap the backend to **AWS S3**, **Azure Blob** (via S3 compatibility), or **GCS** purely via configuration, with no code changes.

## Rationale

- Cloud-agnostic per ADR-0022 — MinIO runs on any Kubernetes cluster.
- The S3 API is the de facto standard for blob storage — every major cloud supports it natively or via a compatibility layer.
- Exit options to AWS S3, Azure Blob, or GCS are trivial (configuration only) — no vendor lock-in.
- MinIO has a mature Kubernetes Operator with erasure coding and automatic healing — operational cost is manageable for a solo developer.
- MinIO licensing: the server is AGPLv3, but we consume it as an **external network service** (not embedded or modified), which is permitted; client libraries are Apache 2.0.
- .NET ecosystem: `AWSSDK.S3`, `Minio.NET`, or FluentStorage all provide identical S3-API access, making the abstraction cheap.
- Prefix-based tenant isolation simplifies GDPR erasure (ADR-0015) — one bulk `DeleteObjects` with a tenant prefix.
- Transactional consistency with PostgreSQL is not required for the blob payloads (logos, assets, exports), so asynchronous object storage is an appropriate fit.

## Alternatives Considered

- **MinIO only, no abstraction:** simpler initially, but forecloses enterprise / managed-cloud deployments and creates tight coupling to one backend.
- **Cloud-native per deployment (raw S3 / Azure Blob / GCS SDKs, no abstraction):** violates ADR-0022; different credentials and APIs per cloud; vendor lock-in at the SaaS layer.
- **PostgreSQL LO / `bytea`:** bloats the DB quickly with documentation assets; slower than S3; expensive per-GB; does not scale past a few tenants with rich docs.
- **SeaweedFS / Ceph:** massively over-engineered for a few-GB-per-tenant workload.
- **Filesystem (local PVCs):** no replication, no bucket semantics, brittle; not scalable across pods or zones.

## Consequences

**Positive:**
- Cloud-agnostic — no vendor lock-in.
- Exit options to any S3-compatible backend via configuration change.
- MinIO Operator reduces operational burden versus a hand-rolled K8s deployment.
- Per-tenant prefix simplifies GDPR cascade deletion.
- Identical `IBlobStorage` interface across dev / staging / prod and across cloud backends.

**Negative / Trade-offs:**
- Additional component to operate in production (MinIO cluster — minimum 4 nodes for erasure-coded HA).
- Abstraction layer is more code to maintain than direct SDK usage.
- Need to handle S3 API nuances consistently (signed URLs, multipart uploads, lifecycle rules) across backends.
- AGPLv3 of the MinIO server requires careful internal compliance posture (do not modify and redistribute).

**Neutral:**
- Bucket-per-tenant is an alternative layout that can be adopted later if stricter isolation becomes necessary (requires IAM-policy work).
- Cold archival (ADR-0020) can use a separate bucket with lifecycle rules — natively supported by the S3 API.

## References

- PRD §7.3 (GDPR erasure scope)
- Phase 0 E-01.F-05.S-04 (right to erasure), E-01.F-01.S-03 (Docker Compose local dev)
- MinIO: https://min.io
- MinIO Kubernetes Operator: https://github.com/minio/operator
- FluentStorage .NET library: https://github.com/robinrodricks/FluentStorage
- S3 API reference: https://docs.aws.amazon.com/AmazonS3/latest/API/
- ADR-0001 (PostgreSQL), ADR-0015 (GDPR cascade deletion), ADR-0020 (cold archival), ADR-0022 (cloud-agnostic K8s), ADR-0026 (proprietary product)
