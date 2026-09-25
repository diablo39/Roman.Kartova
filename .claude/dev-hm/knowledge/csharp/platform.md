# Modern .NET platform patterns

Production C# on the current .NET LTS. Target framework, C# language version, and package
versions are pinned in `knowledge/shared/versions.md` — this file describes the patterns, not the
numbers. Enable nullable reference types and treat warnings as errors on every project.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Project baseline | `knowledge/csharp/platform/project-baseline.md` |
| Dependency injection and testability | `knowledge/csharp/platform/dependency-injection-and-testability.md` |
| Options pattern (`IOptions` family) | `knowledge/csharp/platform/options-pattern-ioptions-family.md` |
| HTTP clients | `knowledge/csharp/platform/http-clients.md` |
| API surface: minimal APIs vs controllers | `knowledge/csharp/platform/api-surface-minimal-apis-vs-controllers.md` |
| Async and cancellation | `knowledge/csharp/platform/async-and-cancellation.md` |
| Modern language features that change design | `knowledge/csharp/platform/modern-language-features-that-change-design.md` |
| EF Core and data access | `knowledge/csharp/platform/ef-core-and-data-access.md` |
| Structured logging | `knowledge/csharp/platform/structured-logging.md` |
| Native AOT and trimming | `knowledge/csharp/platform/native-aot-and-trimming.md` |
