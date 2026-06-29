# project-stats Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `/project-stats` skill that prints reproducible codebase statistics — LOC by language, production vs test, and business/infrastructure/boilerplate/other — from the git-tracked file set.

**Architecture:** A deterministic bash script (`scripts/project-stats.sh`) enumerates `git ls-files`, classifies each path in one pure-string `gawk` pass (language · role · domain), batch-counts non-blank lines for text files, then joins and renders three tables + a headline. A sibling test harness (`project-stats.test.sh`) asserts named real files classify correctly and is the regression test. `SKILL.md` documents when/how to run it and the classification rules.

**Tech Stack:** bash + gawk (both ship with Git Bash on the Windows host and exist on the CI ubuntu runner), git. No `cloc`/`scc`/`tokei` — none are installed.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-06-29-project-stats-skill-design.md` — authoritative.
- **LOC metric:** non-blank physical lines (comments included; no comment stripping).
- **Engine:** `git ls-files` only; tracked files only. The generated web API client is gitignored → absent from counts (state this in output).
- **Shell:** bash, run via Git Bash on Windows. Use `gawk` explicitly (not `awk`) — the renderer relies on `PROCINFO["sorted_in"]`.
- **Domain precedence:** first match wins, in this order: Boilerplate → Business → Infrastructure → Other (evaluated for production files only; test files are not domain-classified).
- **DoD scope (light):** dev tooling, not a product slice. .NET build / real-seam / mutation / container gates are **N/A**. Verification = the test harness passes + an in-script reconciliation assertion + a manual run on this repo. No DoD ledger / `gate-findings.yaml`.
- **Skill location:** `.claude/skills/project-stats/` (matches `coverage-auditor`/`mutation-sentinel` layout). Both scripts live in `.claude/skills/project-stats/scripts/`.

---

### Task 1: Failing test harness (classification fixtures)

Writes the regression test first. It runs the (not-yet-existing) engine in `--debug` mode and asserts that eight representative real files map to the expected `language / role / domain`. It must fail now because the engine is absent.

**Files:**
- Create: `.claude/skills/project-stats/scripts/project-stats.test.sh`

**Interfaces:**
- Consumes (from Task 2): `project-stats.sh --debug` prints one TSV line per tracked file: `path<TAB>language<TAB>kind<TAB>role<TAB>domain`.
- Produces: a runnable test harness; exit 0 = all fixtures correct, exit 1 = a mismatch, exit 3 = engine missing.

- [ ] **Step 1: Write the test harness**

Create `.claude/skills/project-stats/scripts/project-stats.test.sh`:

```bash
#!/usr/bin/env bash
# Regression test for project-stats.sh — asserts representative real files
# classify into the expected (language, role, domain) buckets.
set -uo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$DIR/project-stats.sh"

if [[ ! -x "$SCRIPT" ]]; then
  echo "engine not found / not executable: $SCRIPT" >&2
  exit 3
fi

# One scan, reused by every assertion.
ALL="$("$SCRIPT" --debug)" || { echo "engine --debug failed" >&2; exit 3; }

fail=0
assert() { # path  want_lang  want_role  want_domain
  local p="$1" el="$2" er="$3" ed="$4" row lang role dom
  row="$(printf '%s\n' "$ALL" | awk -F'\t' -v p="$p" '$1==p{print;f=1} END{exit f?0:9}')" \
    || { echo "MISSING from scan: $p"; fail=1; return; }
  IFS=$'\t' read -r _ lang _ role dom <<<"$row"
  if [[ "$lang" != "$el" || "$role" != "$er" || "$dom" != "$ed" ]]; then
    printf 'FAIL %s\n     got [%s/%s/%s] want [%s/%s/%s]\n' "$p" "$lang" "$role" "$dom" "$el" "$er" "$ed"
    fail=1
  else
    printf 'ok   %s -> %s/%s/%s\n' "$p" "$lang" "$role" "$dom"
  fi
}

assert "src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs"                            "C#"         "Production" "Business"
assert "src/Modules/Catalog/Kartova.Catalog.Application/AssignApplicationTeamCommand.cs"      "C#"         "Production" "Business"
assert "src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs"                "C#"         "Production" "Infrastructure"
assert "src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationCountByTeamReader.cs"  "C#"         "Production" "Infrastructure"
assert "src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs"              "C#"         "Test"       "-"
assert "web/src/features/auth/api/session.ts"                                                "TypeScript" "Production" "Business"
assert "web/src/features/auth/api/__tests__/session.test.tsx"                                "TSX"        "Test"       "-"

# A migration path is dynamic — resolve it, then assert it is generated boilerplate.
MIG="$(git ls-files 'src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/*.cs' | grep -v '\.Designer\.cs$' | head -1)"
if [[ -n "$MIG" ]]; then
  assert "$MIG" "C#" "Production" "Boilerplate"
else
  echo "WARN: no Catalog migration found to assert"; fail=1
fi

if [[ "$fail" -eq 0 ]]; then echo "ALL PASS"; else echo "TEST FAILURES"; exit 1; fi
```

- [ ] **Step 2: Make it executable and run it — verify it fails**

```bash
cd "C:/Projects/Private/Roman.Gig2"
chmod +x .claude/skills/project-stats/scripts/project-stats.test.sh
.claude/skills/project-stats/scripts/project-stats.test.sh; echo "exit=$?"
```

Expected: prints `engine not found / not executable: .../project-stats.sh` and `exit=3` (engine does not exist yet).

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/project-stats/scripts/project-stats.test.sh
git commit -m "test(tooling): add project-stats classification fixtures (failing)"
```

---

### Task 2: The engine (`project-stats.sh`)

Implements the full engine so the Task 1 harness passes and a real run prints three reconciling tables.

**Files:**
- Create: `.claude/skills/project-stats/scripts/project-stats.sh`

**Interfaces:**
- Consumes: `git ls-files`, `gawk`.
- Produces:
  - `project-stats.sh` → prints headline + Language / Role / Domain tables to stdout; `--out <file>` also writes them; `--debug` prints the per-file classification TSV (`path<TAB>language<TAB>kind<TAB>role<TAB>domain`) and exits.
  - Non-zero exit if the internal reconciliation assertion fails.

- [ ] **Step 1: Write the engine**

Create `.claude/skills/project-stats/scripts/project-stats.sh`:

```bash
#!/usr/bin/env bash
# project-stats.sh — deterministic codebase statistics from git-tracked files.
# Axes: language · role (production/test) · domain (business/infra/boilerplate/other).
# Deps: git + gawk (both ship with Git Bash). LOC = non-blank physical lines.
# Classification rules are documented in ../SKILL.md and live in classify() below.
set -euo pipefail

OUT=""
DEBUG=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out)   OUT="${2:?--out requires a path}"; shift 2 ;;
    --debug) DEBUG=1; shift ;;
    -h|--help) echo "usage: project-stats.sh [--out <file>] [--debug]"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

command -v gawk >/dev/null 2>&1 || { echo "project-stats: gawk required (ships with Git Bash)" >&2; exit 3; }
cd "$(git rev-parse --show-toplevel)"

# ── 1. classify every tracked path (pure string ops; no file reads) ──
#    TSV: path \t language \t kind(text|binary) \t role \t domain
classify() {
  git ls-files | gawk '
  {
    path=$0
    n=split(path, seg, "/"); base=seg[n]
    ext=""
    if (base ~ /\./) { m=split(base, p, "."); ext=tolower(p[m]) }

    # --- language ---
    lang="Config/Other"; kind="text"
    if      (ext=="cs")  lang="C#"
    else if (ext=="tsx") lang="TSX"
    else if (ext=="ts")  lang="TypeScript"
    else if (ext=="js"||ext=="mjs") lang="JavaScript"
    else if (ext=="md")  lang="Markdown"
    else if (ext=="yaml"||ext=="yml"||ext=="tpl"||base==".helmignore") lang="YAML/Helm"
    else if (ext=="json") lang="JSON"
    else if (ext=="sql")  lang="SQL"
    else if (ext=="html") lang="HTML"
    else if (ext=="css")  lang="CSS"
    else if (ext=="sh")   lang="Shell"
    else if (ext=="ps1")  lang="PowerShell"
    else if (ext=="py")   lang="Python"
    else if (ext=="csproj"||ext=="props"||ext=="slnx"||ext=="xml"||ext=="runsettings") lang="MSBuild/XML"
    else if (base=="Dockerfile"||base==".dockerignore") lang="Dockerfile"
    else if (base=="Makefile") lang="Makefile"
    else if (ext ~ /^(png|jpg|jpeg|gif|ico|svg|webp|woff|woff2|ttf|eot|pdf)$/) { lang="Asset"; kind="binary" }

    # --- role: production vs test (app-code languages only) ---
    code = (lang=="C#"||lang=="TSX"||lang=="TypeScript"||lang=="JavaScript"||lang=="SQL"||lang=="HTML"||lang=="CSS")
    if (path ~ /^docs\//) code=0      # docs/ (incl. mockup .html) are never production
    role="NonCode"
    if (code) {
      role="Production"
      if      (path ~ /(^|\/)tests\//)          role="Test"
      else if (path ~ /\.Tests\//)              role="Test"
      else if (path ~ /\.IntegrationTests\//)   role="Test"
      else if (path ~ /Kartova\.Testing\./)     role="Test"
      else if (path ~ /(^|\/)__tests__\//)      role="Test"
      else if (base ~ /\.(test|spec)\.[a-z]+$/) role="Test"
    }

    # --- domain (production only); FIRST MATCH WINS ---
    domain="-"
    if (role=="Production") {
      if      (path ~ /\/Migrations\// || base ~ /\.Designer\.cs$/ || base ~ /ModelSnapshot\.cs$/ || path ~ /(^|\/)web\/src\/generated\//) domain="Boilerplate"
      else if (path ~ /\.Domain\// || path ~ /\.Application\//) { if (base ~ /(Dto|Request|Response)\.cs$/) domain="Infrastructure"; else domain="Business" }
      else if (path ~ /(^|\/)web\/src\/(features|pages|hooks)\//) domain="Business"
      else if (path ~ /\.Infrastructure/ || path ~ /Kartova\.Api\// || path ~ /\.Contracts\// || base ~ /(Dto|Request|Response)\.cs$/ || path ~ /SharedKernel/ || path ~ /(^|\/)web\/src\//) domain="Infrastructure"
      else domain="Other"
    }
    printf "%s\t%s\t%s\t%s\t%s\n", path, lang, kind, role, domain
  }'
}

CLS="$(classify)"

if [[ "$DEBUG" == "1" ]]; then
  printf '%s\n' "$CLS"
  exit 0
fi

# ── 2. count non-blank + total physical lines for text files (batched) ──
#    TSV: path \t total \t nonblank
LOC="$(printf '%s\n' "$CLS" | gawk -F'\t' '$3=="text"{print $1}' \
  | tr '\n' '\0' | xargs -0 -r gawk '
      FNR==1 { if (f!="") print f"\t"tot"\t"nb; f=FILENAME; tot=0; nb=0 }
      { sub(/\r$/,""); tot++ } NF { nb++ }
      END { if (f!="") print f"\t"tot"\t"nb }
  ')"

# ── 3. join + aggregate + render ──
render() {
  gawk -F'\t' '
    FNR==NR { tot[$1]=$2+0; nb[$1]=$3+0; next }   # LOC stream first
    {
      path=$1; lang=$2; kind=$3; role=$4; dom=$5
      files_total++; L=nb[path]; total_loc+=L
      lf[lang]++; ll[lang]+=L
      if (kind=="binary") asset_files++
      if (role=="Production"||role=="Test") { rf[role]++; rl[role]+=L; code_files++ }
      else { noncode_files++; noncode_loc+=L }
      if (role=="Production" && dom!="-") { df[dom]++; dl[dom]+=L; prod_loc+=L }
    }
    END {
      # ---- reconciliation (the in-script correctness assertion) ----
      s=0; for (k in lf) s+=lf[k]
      if (s!=files_total)                              { print "RECONCILE FAIL: language sum "s" != total "files_total > "/dev/stderr"; exit 1 }
      if ((rf["Production"]+rf["Test"])!=code_files)   { print "RECONCILE FAIL: role split"                      > "/dev/stderr"; exit 1 }
      ds=0; for (k in df) ds+=df[k]
      if (ds!=(rf["Production"]+0))                    { print "RECONCILE FAIL: domain sum "ds" != prod "(rf["Production"]+0) > "/dev/stderr"; exit 1 }

      # ---- headline ----
      bizpct=(prod_loc>0)?100*dl["Business"]/prod_loc:0
      printf "Headline:  %d files · %d LOC · %.0f%% business code (of production)\n\n", files_total, total_loc, bizpct

      # ---- language (sorted by LOC desc) ----
      PROCINFO["sorted_in"]="@val_num_desc"
      printf "Language          Files       LOC\n----------------------------------\n"
      for (k in ll) printf "%-16s %6d %9d\n", k, lf[k], ll[k]
      printf "%-16s %6d %9d\n\n", "TOTAL", files_total, total_loc

      # ---- role ----
      printf "Role              Files       LOC\n----------------------------------\n"
      printf "%-16s %6d %9d\n",   "Production", rf["Production"]+0, rl["Production"]+0
      printf "%-16s %6d %9d\n",   "Test",       rf["Test"]+0,       rl["Test"]+0
      printf "%-16s %6d %9d\n\n", "Non-code",   noncode_files+0,    noncode_loc+0

      # ---- domain (fixed order) ----
      printf "Domain (production)  Files       LOC   %% prod LOC\n------------------------------------------------\n"
      split("Business Infrastructure Boilerplate Other", ord, " ")
      for (i=1;i<=4;i++){ k=ord[i]; pc=(prod_loc>0)?100*dl[k]/prod_loc:0; printf "%-18s %6d %9d %8.0f%%\n", k, df[k]+0, dl[k]+0, pc }
      nb_files=df["Infrastructure"]+df["Boilerplate"]+df["Other"]+0
      nb_loc=dl["Infrastructure"]+dl["Boilerplate"]+dl["Other"]+0
      pc=(prod_loc>0)?100*nb_loc/prod_loc:0
      printf "%-18s %6d %9d %8.0f%%\n", "  non-business", nb_files, nb_loc, pc

      print "\nNotes: LOC = non-blank physical lines (comments included). The generated web"
      print "API client is gitignored and absent from this count. Classification is file-level."
    }
  ' <(printf '%s\n' "$LOC") <(printf '%s\n' "$CLS")
}

if [[ -n "$OUT" ]]; then render | tee "$OUT"; else render; fi
```

- [ ] **Step 2: Make it executable**

```bash
cd "C:/Projects/Private/Roman.Gig2"
chmod +x .claude/skills/project-stats/scripts/project-stats.sh
```

- [ ] **Step 3: Run the test harness — verify it passes**

```bash
.claude/skills/project-stats/scripts/project-stats.test.sh; echo "exit=$?"
```

Expected: eight `ok ...` lines, then `ALL PASS` and `exit=0`. If any line says `FAIL`, the classify globs are wrong — fix `classify()` (do not relax the test) and re-run.

- [ ] **Step 4: Run the engine for real — verify reconciliation + plausible numbers**

```bash
.claude/skills/project-stats/scripts/project-stats.sh; echo "exit=$?"
```

Expected: `exit=0` (no `RECONCILE FAIL`), a Language table led by `C#`, a Role table where `Production` and `Test` are both non-trivial, and a Domain table with all four buckets populated and a `non-business` rollup. Sanity: Language TOTAL files ≈ 1,338.

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/project-stats/scripts/project-stats.sh
git commit -m "feat(tooling): project-stats engine — LOC by language/role/domain"
```

---

### Task 3: Skill document (`SKILL.md`)

Makes `/project-stats` invocable and documents the rules so the buckets are auditable without reading bash.

**Files:**
- Create: `.claude/skills/project-stats/SKILL.md`

**Interfaces:**
- Consumes: `scripts/project-stats.sh` (the engine from Task 2).
- Produces: a `user-invocable` skill that, when invoked, runs the engine and relays its output.

- [ ] **Step 1: Write SKILL.md**

Create `.claude/skills/project-stats/SKILL.md`:

```markdown
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

\`\`\`bash
.claude/skills/project-stats/scripts/project-stats.sh            # print to chat
.claude/skills/project-stats/scripts/project-stats.sh --out stats.md   # also write a file
\`\`\`

Run it, then relay the three tables and the headline. The script self-checks that
every file reconciles into exactly one bucket per axis and exits non-zero on mismatch —
if it fails, report the `RECONCILE FAIL` line rather than presenting partial numbers.

Regression test (run after editing the engine or its rules):

\`\`\`bash
.claude/skills/project-stats/scripts/project-stats.test.sh
\`\`\`

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
```

- [ ] **Step 2: Verify the skill is discoverable**

```bash
cd "C:/Projects/Private/Roman.Gig2"
head -6 .claude/skills/project-stats/SKILL.md
```

Expected: valid frontmatter with `name: project-stats` and `user-invocable: true`. (Invoking `/project-stats` end-to-end is exercised in Task 4.)

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/project-stats/SKILL.md
git commit -m "docs(tooling): /project-stats skill doc + classification rules"
```

---

### Task 4: Final verification + evidence

Confirms the whole skill works end-to-end on this repo and captures the run as evidence.

**Files:**
- Create: `docs/superpowers/verification/2026-06-29-project-stats/run.md` (captured output as evidence)

- [ ] **Step 1: Re-run the test harness (green)**

```bash
cd "C:/Projects/Private/Roman.Gig2"
.claude/skills/project-stats/scripts/project-stats.test.sh; echo "exit=$?"
```

Expected: `ALL PASS`, `exit=0`.

- [ ] **Step 2: Capture a real run as evidence**

```bash
mkdir -p docs/superpowers/verification/2026-06-29-project-stats
{
  echo '# /project-stats — verification run (2026-06-29)'
  echo
  echo '## Test harness'; echo '```'
  .claude/skills/project-stats/scripts/project-stats.test.sh
  echo '```'
  echo '## Full report'; echo '```'
  .claude/skills/project-stats/scripts/project-stats.sh
  echo '```'
} > docs/superpowers/verification/2026-06-29-project-stats/run.md
cat docs/superpowers/verification/2026-06-29-project-stats/run.md
```

Expected: the file contains `ALL PASS` and a full report whose Language TOTAL ≈ 1,338 files and whose Domain table reconciles (no `RECONCILE FAIL`).

- [ ] **Step 3: Shellcheck (advisory, non-blocking)**

```bash
command -v shellcheck >/dev/null 2>&1 \
  && shellcheck .claude/skills/project-stats/scripts/project-stats.sh .claude/skills/project-stats/scripts/project-stats.test.sh \
  || echo "shellcheck not installed — skipping (advisory only)"
```

Expected: clean output, or the "skipping" line. Address any *error*-level findings; warnings are optional.

- [ ] **Step 4: Commit evidence**

```bash
git add docs/superpowers/verification/2026-06-29-project-stats/run.md
git commit -m "docs(tooling): project-stats verification run evidence (2026-06-29)"
```

---

## Self-Review

**1. Spec coverage:**
- LOC per language → Task 2 Language table. ✓
- Production vs test → Task 2 Role table. ✓
- Business vs infra/boilerplate → Task 2 Domain table (4 buckets + rollup), rules in Task 3. ✓
- Deterministic / zero-install → bash + git + gawk; no model counting. ✓
- Non-blank LOC, binaries excluded, generated client noted → classify()/render() + footer. ✓
- Reconciliation self-check → render() END assertions (Task 2). ✓
- Spot-check fixtures → Task 1 harness. ✓
- `--out` flag → Task 2. ✓ (Per-module breakdown deliberately out of scope per spec.)
- Verification (light DoD) → Task 4. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete, runnable content. No "add error handling" hand-waves.

**3. Type consistency:** `--debug` TSV column order (`path · language · kind · role · domain`) is identical in `classify()` (Task 2) and consumed by the harness `IFS` read (Task 1: `_ lang _ role dom`). Bucket spellings (`Business`/`Infrastructure`/`Boilerplate`/`Other`, `Production`/`Test`/`NonCode→"Non-code"` label) match between engine, harness asserts, and SKILL.md rules.
