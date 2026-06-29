# Design — `project-stats` skill

**Date:** 2026-06-29
**Type:** Developer tooling (not a product slice — no owning Epic/Feature/Story)
**Status:** Approved (brainstorming) → pending plan

## Problem

There is no one command that reports the codebase's size and shape. The owner wants
on-demand statistics on three independent axes:

1. **Lines of code per language.**
2. **Production vs test** code.
3. **Business vs infrastructure/boilerplate** code.

The numbers must be **reproducible** (no model-counted estimates that drift run to run)
and require **zero setup** (no `cloc`/`scc`/`tokei` install — none are present on the host).

## Goal

A project skill, `/project-stats`, that runs a deterministic shell script over the
git-tracked file set and prints three tables plus a headline summary.

## Non-goals (YAGNI)

- **Comment/blank-line separation per file.** Hand-rolling cannot strip comments
  reliably across C#/TS/YAML/SQL. Headline metric is non-blank physical lines; a
  comment breakdown is explicitly out of scope (would require `scc`/`cloc`).
- **Per-module breakdown** (`--module Catalog`). A natural later extension; not in v1.
- **Historical/trend tracking** or CI gating on the numbers.
- **Counting untracked or gitignored files.** The skill reports the *committed* repo;
  the generated API client is gitignored and therefore absent from the count (noted in
  output rather than special-cased).

## Counting engine

- **Source of truth:** `git ls-files` (tracked files only). Deterministic, fast, needs
  nothing beyond git.
- **Shell:** bash. Matches `scripts/ci-local.sh`, runs in Git Bash on the Windows host
  and on the CI ubuntu runner. (`git ls-files | awk` is far cleaner here than
  PowerShell; the global PowerShell-default preference is consciously overridden for
  this script.)
- **LOC metric:** **non-blank physical lines** = total lines minus whitespace-only
  lines, via `awk 'NF{c++}END{print c}'`. Total physical lines also reported. No
  comment stripping (see non-goals).
- **Binary/asset files** (`png jpg jpeg gif ico svg webp woff woff2 ttf eot pdf`) are
  **excluded from LOC** and reported only as a file count.

## The three axes

Each axis is computed independently; a file contributes to all axes it qualifies for.

### Axis 1 — Language (all tracked text files)

Mapped by extension, with filename fallbacks for extensionless files:

| Extension / filename | Language |
|---|---|
| `.cs` | C# |
| `.tsx` | TSX (React) |
| `.ts` | TypeScript |
| `.js` `.mjs` | JavaScript |
| `.md` | Markdown |
| `.yaml` `.yml` `.tpl` `.helmignore` | YAML/Helm |
| `.json` | JSON |
| `.sql` | SQL |
| `.html` | HTML |
| `.css` | CSS |
| `.sh` | Shell |
| `.ps1` | PowerShell |
| `.py` | Python |
| `.csproj` `.props` `.slnx` `.xml` `.runsettings` | MSBuild/XML |
| `Dockerfile` `.dockerignore` | Dockerfile |
| `Makefile` | Makefile |
| `.editorconfig` `.gitignore` `.gitattributes` `.claudeignore` `.conf` `.config` `.example` `.development` `.txt` | Config/Other |

Unknown extensions fall through to an `Other` language row (so totals always reconcile).

### Axis 2 — Role (code files only)

`Test` if any of:
- C# under a project dir matching `*.Tests` or `*.IntegrationTests`
- path under `/tests/`
- project/path matching `Kartova.Testing.*`
- web file matching `*.test.*`, `*.spec.*`, or under `__tests__/`

Otherwise a **code** file is `Production`. Non-code files (Markdown, YAML, JSON,
config, assets) are outside the prod/test split and reported on a `Non-code` line so
the role table reconciles to the code total.

"Code" languages for this axis: C#, TSX, TypeScript, JavaScript, SQL, Shell,
PowerShell, Python, HTML, CSS.

### Axis 3 — Domain (production files only)

Test files get their own bucket and are **not** domain-classified. Production files
fall into exactly one of four buckets, evaluated **top to bottom (first match wins)**:

| Order | Bucket | Rules (first match wins) |
|---|---|---|
| 1 | **Boilerplate** (generated) | `**/Migrations/**`, `*.Designer.cs`, `*ModelSnapshot.cs`, generated API client path (gitignored — usually absent) |
| 2 | **Business** | `**/*.Domain/**`, `**/*.Application/**` (minus `*Dto`/`*Request`/`*Response` filenames), web `web/src/{features,pages,hooks}/**` |
| 3 | **Infrastructure** (hand-written plumbing) | `**/*.Infrastructure*/**`, `**/Kartova.Api/**` (incl. `Program.cs`, `*Module.cs`), `**/*.Contracts/**` + `*Dto`/`*Request`/`*Response` anywhere, `**/SharedKernel*/**`, remaining `web/src/**` (api-client wiring, app shell) |
| 4 | **Other** | everything else: `docs/**`, `scripts/**`, build/deploy config (csproj/props/yaml/Helm/Docker/json/lockfiles), CI/editor config, assets |

**Precedence rationale:** Boilerplate is checked *before* Business/Infrastructure so a
generated migration living inside an `*.Infrastructure` project is counted as generated,
not infra. DTO filename patterns demote contract types out of Business even when they
sit in an `*.Application` project.

## Output

Printed to chat (default). Three Markdown tables + a headline line:

```
Headline:  1,338 files · ~95k LOC · 34% business code (of production)

Language        Files     LOC
C#                555   48,200
TSX               249   22,100
...

Role            Files     LOC
Production        612   41,000
Test              388   38,500
Non-code          290      n/a

Domain (production)   Files     LOC      % of prod LOC
Business              180    14,000            34%
Infrastructure        260    19,500            48%
Boilerplate (gen)      90     6,000            15%
Other                  82     1,500             3%
  └ non-business (Infra+Boilerplate+Other): 66%
```

(Numbers above are illustrative placeholders — the script produces real values.)

Optional single flag: `--out <path>` also writes the same report as Markdown. Whole-repo
only in v1.

## Files

| Path | Purpose |
|---|---|
| `.claude/skills/project-stats/SKILL.md` | Skill entry point — frontmatter (`name`, `description`, `argument-hint`, `user-invocable: true`), when-to-use, how it runs the script, and the **human-readable classification-rule reference** (so buckets are auditable without reading bash). |
| `.claude/skills/project-stats/scripts/project-stats.sh` | The deterministic engine. Classification globs live in a clearly-commented, editable block at the top so rules can be tuned in one place. |

(No separate `knowledge/` file — the rule reference lives in `SKILL.md` to keep the
surface minimal.)

## Verification (DoD scope: light)

This is a self-contained bash script + skill doc. It touches no HTTP/auth/DB/middleware,
so the product DoD's heavy gates — full .NET solution build, real-seam integration tests,
mutation loop, container build — are **N/A**. The meaningful bar:

1. **Reconciliation self-check (in-script):** the script asserts that
   - every counted text file lands in exactly one Language bucket (sum of language file
     counts == total text files), and
   - every code file lands in exactly one Role bucket, and every production file in
     exactly one Domain bucket.
   On mismatch it prints the offending file(s) and exits non-zero. This is the script's
   own correctness test — no separate test project.
2. **Manual run on this repo** as evidence: `/project-stats` produces a clean report with
   reconciling totals; spot-check ~5 representative files (a `.Domain` entity, an
   `.Application` handler, a migration, a `*Dto`, a test) land in the expected buckets.
3. **Shellcheck-clean** (if `shellcheck` is available) — advisory, not blocking.

No DoD ledger / `gate-findings.yaml` is created (those are for product slices).

## Known limitations (stated in output footer)

- LOC counts blank-excluded physical lines, **including comments** — not a "pure logic"
  count.
- The generated API client is gitignored and absent from a committed run; the Boilerplate
  bucket will under-report generated code by exactly that client.
- Classification is **file-level**, not line-level — a file with mixed business + plumbing
  is counted whole into its first-matching bucket.
