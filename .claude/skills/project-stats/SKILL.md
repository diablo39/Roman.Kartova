---
name: project-stats
description: "Report codebase statistics — LOC by language, production vs test, and business/infrastructure/boilerplate/other — from the git-tracked file set. Use when asked for project size, LOC breakdown, code composition, or 'how big is this codebase'."
argument-hint: "Optional: --out <path> to also write the report to a file"
user-invocable: true
---

# Project Stats

Deterministic codebase statistics, computed from `git ls-files` by a bash script —
no model-side counting, so numbers are reproducible run to run.

## When to use

- "How many lines of code?" / "LOC by language?" / "how big is the project?"
- "How much is tests vs production?"
- "How much is business logic vs infrastructure/boilerplate?"

## How to run

From the repo root (Git Bash):

```bash
.claude/skills/project-stats/scripts/project-stats.sh            # print to chat
.claude/skills/project-stats/scripts/project-stats.sh --out stats.md   # also write a file
```

Run it, then relay the three tables and the headline. The script self-checks that
every file reconciles into exactly one bucket per axis and exits non-zero on mismatch —
if it fails, report the `RECONCILE FAIL` line rather than presenting partial numbers.

Regression test (run after editing the engine or its rules):

```bash
.claude/skills/project-stats/scripts/project-stats.test.sh
```

## What it reports

| Axis | Buckets |
|------|---------|
| Language | by extension/filename (C#, TSX, TypeScript, Markdown, YAML/Helm, JSON, SQL, Dockerfile, …) |
| Role | Production · Test · Non-code (docs/config) |
| Domain (production only) | Business · Infrastructure · Boilerplate (generated) · Other, plus a non-business rollup |

## Classification rules (auditable reference)

LOC = **non-blank physical lines** (comments included; no comment stripping).
Binary/asset files are excluded from LOC and not shown in the tables.

**Role** — a file is **Test** if its path is under `tests/`, a `*.Tests`/`*.IntegrationTests`
project, `Kartova.Testing.*`, an `__tests__/` dir, or matches `*.test.*` / `*.spec.*`.
Only app-code languages (C#, TSX, TS, JS, SQL, HTML, CSS) count toward Production/Test;
anything under `docs/` and all non-code files are **Non-code**.

**Domain** (production files only; **first match wins**):

1. **Boilerplate** (generated): `**/Migrations/**`, `*.Designer.cs`, `*ModelSnapshot.cs`,
   generated web API client (gitignored → usually absent).
2. **Business**: `**/*.Domain/**`, `**/*.Application/**` (minus `*Dto`/`*Request`/`*Response`
   filenames), web `web/src/{features,pages,hooks}/**`.
3. **Infrastructure** (hand-written plumbing): `*.Infrastructure*`, `Kartova.Api/**`
   (incl. `Program.cs`, `*Module.cs`), `*.Contracts/**` + `*Dto`/`*Request`/`*Response`,
   `SharedKernel*`, remaining `web/src/**`.
4. **Other**: anything else classified as production.

## Limitations

- File-level, not line-level: a mixed file is counted whole into its first-matching bucket.
- No comment/blank separation (would need `scc`/`cloc`, which are not installed).
- Counts tracked files only; the gitignored API client is not included.

## Tuning

Edit the `classify()` globs in `scripts/project-stats.sh` (one commented block), then
re-run `project-stats.test.sh` and add a fixture for any rule you change.
