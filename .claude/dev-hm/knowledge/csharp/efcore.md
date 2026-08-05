# EF Core patterns

Production data access with EF Core on the current LTS (versions in `knowledge/shared/versions.md`).
This file covers the depth behind the summary rows in `knowledge/csharp/platform.md` and
`knowledge/csharp/review-checklist/data-access.md`: querying, bulk operations, concurrency,
interceptors, and migrations that survive rolling deploys. SQL parameterization rules stay in the
review checklist (SEC-CS-001); PostgreSQL-side schema and locking detail lives in
`knowledge/postgresql/schema-design.md`.

The choice of persistence strategy itself — EF Core vs a micro-ORM vs raw ADO.NET, one context vs
per-bounded-context contexts, relational vs JSON-column document shapes — is a one-way door once
data exists. Run it through `knowledge/shared/three-framing-analysis.md` and record the ADR;
everything below assumes EF Core has already won that decision.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Context lifetime and registration {#lifetime} | `knowledge/csharp/efcore/lifetime.md` |
| Query patterns {#queries} | `knowledge/csharp/efcore/queries.md` |
| N+1: detection and fixes {#n-plus-one} | `knowledge/csharp/efcore/n-plus-one.md` |
| Bulk updates and deletes {#execute-update} | `knowledge/csharp/efcore/execute-update.md` |
| Optimistic concurrency {#concurrency} | `knowledge/csharp/efcore/concurrency.md` |
| Interceptors {#interceptors} | `knowledge/csharp/efcore/interceptors.md` |
| Migrations for rolling deploys {#migrations} | `knowledge/csharp/efcore/migrations.md` |
| Testing data access {#testing} | `knowledge/csharp/efcore/testing.md` |
