# Runtime and language anchors

| Stack | Anchor | Note |
|---|---|---|
| TypeScript | 7.0 GA (Jul 2026), strict mode | Native Go compiler ("Corsa"), 8–12× faster builds; 7.1 (~Oct 2026) restores Vue/Svelte/Astro editor support |
| Node.js | 24 Active LTS; 22 Maintenance; 26 Current (LTS Oct 2026) | New cadence from Oct 2026: annual majors, all become LTS, 36-month support |
| React | 19.2 (19.2.7) | React Compiler 1.0 stable (Oct 2025) |
| TS build/test | Vite 8.0 (Rolldown/Rust bundler; 8.1.4); Vitest 4.x (4.1.10; 5.0 beta); ESLint 9.x flat config only; Playwright 1.61 | typescript-eslint recommended-type-checked baseline |
| TS libraries | Zod v4 (+ Zod Mini); TanStack Query v5; Prisma 7 (pure-TS client, ~7.6); Drizzle ~0.45 | |
| Python | 3.14 stable (current 3.14.6; 3.13 supported) | Free-threaded build officially supported (PEP 779): optional python3.14t, GIL default on |
| Python toolchain | uv 0.11.x (workspaces + root uv.lock); ruff 0.15.x (2026 style guide); pytest 9.x + pytest-asyncio 1.4.0 (min pytest 8.4) | Type checkers: mypy (mature default), pyright (~96% conformance), pyrefly 1.0 stable, ty (Astral) beta |
| Python libraries | testcontainers-python 4.14.x; pydantic 2.13.x (v2 API) | |
| .NET / C# | .NET 10 current LTS (Nov 2025 → Nov 2028); C# 14; EF Core 10 LTS | C# 14: extension members, field-backed properties, null-conditional assignment |
| .NET AOT | ASP.NET Core Native AOT: minimal APIs partial, gRPC/workers supported | MVC/Blazor Server unsupported |
| .NET testing | MSTest 4.2.x (TestFramework/TestAdapter/Analyzers); NSubstitute 5.3; Testcontainers .NET 4.x; coverlet.collector 6.0.x | VSTest runner via Microsoft.NET.Test.Sdk; `Assert.ThrowsExactly` is the MSTest 4 idiom |
| .NET diagnostic tools | dotnet-counters / dotnet-trace / dotnet-dump / dotnet-gcdump / dotnet-stack — dotnet/diagnostics v10.0.x train (Jun 2026); SOS ships the same train | Install per tool via `dotnet tool install -g`; PerfView remains Windows-only |
| PostgreSQL | 18 current major (2025-09-25); minors 18.4/17.10/16.14/15.18/14.23; supported majors 14–18 (14 EOL Nov 2026); PG19 Beta 1 Jun 2026 | PG18 headline: async I/O (io_method worker/io_uring), B-tree skip scan, uuidv7(), virtual generated columns, OAuth 2.0 auth, EXPLAIN ANALYZE BUFFERS default |
| PostgreSQL ecosystem | Pooling: PgBouncer de facto standard (transaction mode); PgCat Rust alternative (read/write split) | Extensions: pgvector (HNSW), pg_partman, pg_stat_statements, PostGIS, TimescaleDB |
| TS server libs | Fastify 5.x (5.10); pino 10.x; piscina 5.x | Node backend default stack: schema-first routes, JSON logging, worker-thread pools |
| Next.js | 16.2 | The current App Router generation — mainstream host for RSC and server actions |
| TS contract/data libs | tRPC 11.x (11.18); openapi-typescript 7.x; react-hook-form 7.x; TanStack Virtual 3.x; MSW 2.x | Contract-style and client-state plumbing anchors |
| TS publishing tooling | pnpm 11.x current major (10.x still common); tsdown 0.22.x; publint 0.3.x; @arethetypeswrong/cli 0.18.x | Library lane: bundle, lint package exports, check type resolution |
| Python async/web | anyio 4.x (4.14); FastAPI 0.139.x | anyio underpins httpx, Starlette, and FastAPI |
| Python supply chain | `uv audit` in preview (Jun 2026); OSV malware lookup on sync opt-in (`UV_MALWARE_CHECK=1`); `uv_build` backend stable | pip-audit-class scanning inside uv; also flags deprecated/abandoned packages |
| Mutation testing | Stryker (JS/C#), mutmut (Python) | Versions unpinned — track the tool's own release notes |
| .NET resilience/observability | OTel .NET 1.16 (Jun 2026); Polly 8.7; Microsoft.Extensions.Resilience 10.8 | |
| .NET perf/gRPC | BenchmarkDotNet 0.15.x (0.15.8); Grpc.AspNetCore 2.80 | |
| PostgreSQL HA/backup | Patroni 4.1.x; pgBackRest 2.54.x | The replication-backup tool pair |
| PostgreSQL extension lines | pgvector 0.8.x (0.8.2); PostGIS 3.6; pg_partman 5.4.x | |
| API contract tooling | buf 1.71; Spectral 6.16; oasdiff 1.23 | Protobuf breaking-change lint, spec lint, spec diff |
| Architecture fitness tooling | ArchUnit 1.4.x; dependency-cruiser 18.x | Dependency-rule tests for JVM and JS/TS |
| CDC tooling | Debezium 3.4.x stable (3.6.0 latest, Jul 2026) | Log-based change data capture |
| Docs site generators | Docusaurus 3.10; MkDocs Material in maintenance (fixes through at least Nov 2026), successor Zensical (MIT) in compatibility phase, pre-parity | Selection guidance in the docs-toolchains knowledge |
| Docs QA tooling | Vale 3.15; lychee 0.24; markdownlint-cli2 0.23; mike 2.2 | Prose lint, link check, markdown lint, versioned publishing |
| Supply-chain signing/policy | Sigstore cosign 3.1 (Jun 2026); Kyverno 1.18 | Sign artifacts, verify at admission |
| GitHub rulesets | Generally available; organization-level, layerable | Succeed classic branch protection; the protection mechanism the CI quality gates cite |
