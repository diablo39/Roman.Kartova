# ADR-0089: Use `.slnx` Solution File Format (Not Classic `.sln`)

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Backend Architecture
**Related:** ADR-0027 (.NET 10), ADR-0082 (Modular monolith — solution layout), ADR-0028 (Clean Architecture per module)

## Context

.NET 10 ships a new solution file format, `.slnx`, as the default for `dotnet new sln`. It is an XML-based format that replaces the classic `.sln` MSBuild text format used since Visual Studio 2002. Both formats are fully supported by `dotnet` CLI; Visual Studio 2022 17.12+ and Rider 2024.3+ read both natively. Over time the `.sln` format is expected to reach end-of-life.

Kartova's modular monolith (ADR-0082) is projected to grow to ~40 csproj files across 12 bounded-context modules plus shared primitives, tests, migrator, and composition root. Solution-file churn will be frequent as modules expand and test projects are added. The format choice affects git-diff readability, merge-conflict frequency, and long-term maintainability.

A decision surfaced during Slice 1 implementation (walking skeleton): `dotnet new sln` defaulted to `.slnx`; the initial Task 2 implementer forced classic `.sln` via `--format sln` because the plan's verification step referenced the classic `Microsoft Visual Studio Solution File, Format Version 12.00` header. The user subsequently requested switching back to the default `.slnx`.

## Decision

Use the **`.slnx`** format for `Kartova.slnx` (the single solution file at repo root). Do **not** use classic `.sln`.

Generate via:

```bash
dotnet new sln --name Kartova --output .
```

(no `--format` flag — `.slnx` is the default on .NET 10 SDK)

Add projects via:

```bash
dotnet sln Kartova.slnx add <path-to-csproj>
```

CI, Dockerfiles, Makefile, and all documentation reference `Kartova.slnx`.

## Rationale

- **Readable XML, clean git diffs** — adding/removing a project produces a small, human-readable XML change. Classic `.sln` produces a noisy multi-line delta with GUIDs, project sections, and nested configuration blocks. At 40 csprojs this matters.
- **.NET 10 SDK default** — going with the default avoids friction on every `dotnet new sln` and new team members' onboarding surprises.
- **Forward direction** — Microsoft is investing in `.slnx` (Visual Studio tooling, SDK enhancements). Classic `.sln` is in maintenance mode; eventual deprecation is plausible within Kartova's planning horizon (multi-year MVP → scale).
- **Tooling parity** — every `dotnet sln` / `dotnet build` / `dotnet test` operation is format-agnostic. No capability loss.
- **Cloud-native CI friendliness** — GitHub Actions, Azure Pipelines, and container-based builds treat `.slnx` identically to `.sln`; no downstream changes.

## Alternatives Considered

- **Classic `.sln`** — more battle-tested tooling (older Visual Studio versions, some third-party IDE plugins) but at significant daily-diff cost as the solution grows. Rejected: Kartova's solo-dev author uses VS 2022 17.14+ and Rider 2024.3+, both of which handle `.slnx` natively.
- **Multiple per-module `.slnx` / `.sln` files** — considered for very large monoliths to speed up IDE load. Premature for MVP; single solution works until ~60+ projects. Revisit if IDE load times become painful.
- **No solution file at all** (pure `dotnet build <csproj>` commands) — unworkable for IDE navigation and `dotnet test` discovery across modules. Rejected.

## Consequences

**Positive:**
- Smaller, readable diffs when adding/removing/renaming projects — meaningful when the modular monolith reaches 40+ csprojs
- No manual `--format sln` flag on solution creation — uses default tooling path
- Aligned with modern `.NET` toolchain direction; less refactoring risk when Microsoft promotes `.slnx` further

**Negative / Trade-offs:**
- Contributors using pre-17.12 Visual Studio or pre-2024.3 Rider cannot open the solution without upgrading. Acceptable for a 2026-founded project; documented as a prerequisite in `README.md`.
- Some older third-party analysis tools (e.g., older SonarQube scanners, legacy code-metrics utilities) may not understand `.slnx`. If such a tool blocks a later decision, pin the tool version or use `dotnet sln Kartova.slnx migrate` to produce a temporary classic `.sln` for that tool's pass.
- Slightly smaller community knowledge base (Stack Overflow answers still reference `.sln` more often) — not expected to matter since format is transparent to most operations.

**Neutral:**
- `.slnx` files can be migrated back to `.sln` with `dotnet sln migrate` if the decision ever needs reversing. Reversible at low cost.

## Implementation Notes

- Current repo: `Kartova.slnx` contains folder organization (e.g., `<Folder Name="/src/">`) that classic `.sln` emulates with GUID-keyed SolutionItems — the XML is materially cleaner.
- `dotnet sln Kartova.slnx add <csproj>` preserves folder groupings when the added csproj path implies a folder (e.g., `src/Kartova.SharedKernel/...` puts the project under `/src/` automatically).
- Git Bash on Windows: when running `dotnet` via `cmd`, use `cmd //c` (double slash) to bypass MSYS path translation. Single-slash `cmd /c` mangles the first argument. See CLAUDE.md Conventions.

**CI reference (from `.github/workflows/ci.yml`):**

```yaml
- run: dotnet restore Kartova.slnx
- run: dotnet build Kartova.slnx --configuration Release --no-restore
```

**Makefile reference:**

```makefile
test:
	cmd /c dotnet test Kartova.slnx --configuration Release
```

## References

- `Kartova.slnx` (repo root) — current solution file
- ADR-0082 (Modular monolith — references solution file format)
- .NET 10 SDK release notes: https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10
- `dotnet sln` CLI reference: https://learn.microsoft.com/dotnet/core/tools/dotnet-sln
