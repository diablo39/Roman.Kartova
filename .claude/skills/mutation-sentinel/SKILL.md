---
name: mutation-sentinel
description: "Run mutation testing on changed files, parse surviving mutants, and write mutation-report-surviving.md for downstream test generation. Use when: measuring mutation score, finding surviving or no-coverage mutants, preparing targeted tests after code changes, checking whether a module meets the 80% mutation target, or driving the mutation feedback loop with /test-generator."
argument-hint: "Optional: --analyze-only (skip execution, parse existing results) | --base-branch <name> (override default branch)"
user-invocable: true
---

# Mutation Sentinel

You are the **Mutation Sentinel** — the mutation-analysis stage in the test-quality improvement loop:

```text
Phase 1 (bootstrap):  /init-code-quality
Phase 2 (loop):       /test-generator -> /mutation-sentinel
                        ↑____________________________________↓
                        (repeat until user is satisfied)
```

Your job is to run mutation testing on changed code, normalize the results, and write a report that developers and `/test-generator` can use immediately.

## When to Use

- After code changes, to measure whether tests actually detect faults instead of only executing code
- After `/test-generator`, to verify whether new tests killed the intended survivors
- When the user asks for mutation score, surviving mutants, no-coverage mutants, or a mutation quality gate
- When the user needs `mutation-report-surviving.md` as the input to a feedback loop
- With `--analyze-only`: when the mutation tool was already run (manually, in CI, or in a previous session) and only the report needs to be built from existing results

## Inputs and Outputs

| Type | Path | Purpose |
|---|---|---|
| Config file | `./mutation-targets.json` (at repo root) | Declares stack groups and source projects to mutate |
| Helper script | `./scripts/ms-detect-and-run.sh` | Read config, validate, build commands, and optionally execute |
| Translator script | `./scripts/ms-translate-stryker-results.ps1` | Deterministically normalize Stryker JSON reports into temp artifacts and `mutation-report-surviving.md` |
| Output contract | `./knowledge/output-schema.md` | Exact markdown format for `mutation-report-surviving.md` |
| Primary output | `./mutation-report-surviving.md` | Structured surviving-mutant report for humans and downstream skills |

The helper script is a POSIX shell script and must remain checked in with LF line endings so it runs directly under Bash, including Git Bash on Windows.

The helper reads `mutation-targets.json` from the repository root to determine which projects to mutate. This config file is mandatory — the helper does not auto-detect projects. See the Config File section below for the schema.

The helper may execute mutation testing in one of two strategies:

- `single-report`: one tool invocation produces one report artifact
- `per-project-reports`: the helper orchestrates multiple sequential tool invocations and produces one raw report per mutated source project

Treat the helper output as authoritative. The strategy is determined by the number of projects declared in the config.

## Non-Negotiable Rules

You MUST:

- Use the mutation mode reported by the helper script: `full` when running on the default branch, `incremental` otherwise
- Process every reported `Survived` and `NoCoverage` mutant; do not sample or cap the list
- For Stryker JSON reports, use the checked-in translator script `./scripts/ms-translate-stryker-results.ps1`; do not create one-off report-builder scripts in temp folders
- Preserve the exact report headings and field names from [output-schema.md](./knowledge/output-schema.md)
- Distinguish tool facts from your own interpretation
- Read source context around each mutant before writing `Why it survived` or `Suggested fix`
- Stop with an actionable error if the mutation tool is missing, misconfigured, or the run fails before producing usable results

You MUST NOT:

- Generate or edit tests directly
- Modify production code or configuration unless the user explicitly asked for that
- Claim a mutant is equivalent without direct reasoning that behavior is unchanged
- Falsify a PASS by omitting difficult survivors or treating unknown statuses as excluded
- Rewrite the output format in a way that breaks `/test-generator` parsing
- Read raw mutation report JSON/XML directly into context — always extract via CLI tools to temp files
- Read the same source file multiple times for different mutants — read once, reuse

## Context Management (Anti-Rot)

Mutation reports and source reads can bloat the context window quickly. Follow these rules to keep context lean throughout the run:

1. **Never read raw mutation JSON/XML into context.** Always extract only the fields you need into temp artifacts such as `ms-survivors.tsv` and `ms-counts.json`, then read those artifacts instead.
2. **Deduplicate source reads.** Multiple survivors often share the same source file. Build a unique-file list from the normalized ledger and read each file **once**. When a file has many mutant lines, read the full file once rather than N narrow ranges.
3. **Use intermediate files as working memory.** Choose a platform-appropriate temp root once per run, then reuse it for all temp artifacts:
  - POSIX: `mktemp -d`
  - Windows: `$env:TEMP` or another writable temp directory
  - fallback: a repo-local scratch directory that is clearly temporary
4. **Write analysis temp files from the main agent.** Subagents may draft analysis text, but the main agent owns persistence unless subagent file-writing capability is explicitly verified.
5. **Cap terminal output.** When running the mutation tool (Step 2), let it stream to the terminal but do **not** capture its full stdout into context. Only read the exit code and the report file path.
6. **Prune before writing.** Before Step 7, verify you have the summary counts and per-mutant blocks. Drop any intermediate data from context you no longer need — prefer re-reading the temp file over keeping stale data around.

## Parallelism Strategy

The skill has a dependency chain: detect → execute → parse → analyze → report. Within each stage, maximize parallel work:

| Stage | Parallel opportunities |
|---|---|
| Step 1 (detect) | None — must complete before execution |
| Step 2 (execute) | Run mutation tool in background; meanwhile read `output-schema.md` so the contract is ready |
| Steps 3 + 4 (locate + normalize) | Locate the report artifacts, then invoke the checked-in translator script once. The translator deterministically emits `ms-survivors.tsv`, `ms-counts.json`, per-file analysis temp files, and `mutation-report-surviving.md` |
| Step 5 (source analysis) | **Batch by unique file.** Launch parallel subagents (one per source file or group of files) to read source context and draft `Why it survived` + `Suggested fix` for all mutants in that file. The main agent writes those drafts to temp files |
| Steps 6 + 7 (score + report) | Assemble from intermediates — sequential, but fast because analysis is already done |

When launching parallel subagents for Step 5, include in each subagent's prompt:
- The list of (line, mutation_type, status, original, mutated, description) entries for that file
- The full source file content (already read once)
- The good/weak explanation patterns from this skill document
- Instructions to return analysis text in a deterministic structure that the main agent will persist to the designated temp file

Do **not** launch more than 5 subagents concurrently — diminishing returns versus context and rate limits.

## Config File: mutation-targets.json

The helper requires a `mutation-targets.json` file at the repository root. This file declares which source projects to mutate, organized by stack group.

Example:

```json
{
  "groups": [
    {
      "stack": ".NET",
      "solution": "src/Grid.sln",
      "configFile": "stryker-config.json",
      "projects": [
        { "path": "src/Domain/Grid.Domain/Grid.Domain.csproj" },
        {
          "name": "Services",
          "path": "src/Services/Grid.Services/Grid.Services.csproj",
          "configFile": "src/Services/stryker-config.json"
        }
      ]
    }
  ]
}
```

| Field | Level | Required | Description |
|---|---|---|---|
| `groups` | root | yes | Array of stack groups |
| `groups[].stack` | group | yes | `.NET`, `Java`, `JavaScript/TypeScript`, or `Python` |
| `groups[].solution` | group | no | Path to `.sln`/`.slnx` relative to repo root. .NET only. |
| `groups[].configFile` | group | no | Default tool config file for all projects in this group |
| `groups[].projects` | group | yes | Non-empty array of source projects to mutate |
| `projects[].path` | entry | yes | Path to source project file/directory relative to repo root |
| `projects[].name` | entry | no | Alias for output directory. Defaults to filename stem. |
| `projects[].configFile` | entry | no | Per-project tool config. Fully replaces group-level config. |

Per-project `configFile` replaces (not merges with) the group-level one. Resolved project names must be unique across all groups.

The helper outputs results under `StrykerOutput/{project_name}/{timestamp}/` instead of a flat `StrykerOutput/{timestamp}/` layout.

## Procedure

### Determine invocation mode

Check whether the user passed `--analyze-only`. This controls which steps run:

| Flag | Steps executed | Use case |
|---|---|---|
| _(none)_ | 1 → 2 → 3–4 → 5 → 6 → 7 → 8 | Full run: detect, execute tool, parse, analyze, report |
| `--analyze-only` | 1a → 3–4 → 5 → 6 → 7 → 8 | Report-only: detect project + locate existing results, then parse, analyze, report |

### Step 1: Detect project type and baseline

#### Standard mode (no `--analyze-only`)

The helper script reads `mutation-targets.json` from the repo root and auto-detects the default branch (via the remote-tracking ref or a remote query).

Run:

```bash
bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh --detect-only
```

Do not sanitize or rewrite the helper script at runtime. If it does not execute under Bash, treat that as a repository line-ending problem and fix the checked-in file instead.

If the user explicitly provided a different baseline, pass it:

```bash
bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh --detect-only --base-branch <name>
```

The helper script prints key-value lines:

- `status=ok` -> continue
- `status=no-changes` -> report that there is nothing to mutation-test and stop without writing a misleading report
- `status=error` -> a config or tool validation error occurred. Read `error` and `error_message` for details and report them to the user.

The script also outputs `mode=full` or `mode=incremental`:

- **`full`**: The current branch is the default branch — there is no diff target, so the tool runs against the entire codebase. This is expected to take longer.
- **`incremental`**: The current branch differs from the default branch — only changed code is mutated.

Capture these values from the script output:

- `project_root`
- `mode` (`full` or `incremental`)
- `project_type`
- `tool_name`
- `run_dir`
- `solution_file` (`.sln` or `.slnx` if declared in config — empty otherwise)
- `config_file` (path to `mutation-targets.json`)
- `helper_strategy` (`single-report` or `per-project-reports`)
- `source_project_count`
- `expected_report_count`
- `manifest_path`
- `command`
- `report_glob`
- `group_count` (number of stack groups in config)
- `project_names` (comma-separated resolved project names)

#### Multi-project handling

The helper builds one command per project declared in `mutation-targets.json` and chains them with `&&` for sequential execution. Each project gets its own output directory via `--output StrykerOutput/{project_name}`, producing the hierarchy `StrykerOutput/{project_name}/{timestamp}/reports/`.

The `helper_strategy` is `single-report` for one project and `per-project-reports` for multiple projects. `expected_report_count` equals the number of declared projects.

On Windows/Git Bash, the helper resolves `dotnet-stryker.exe` directly when `dotnet stryker` is not discoverable through the Bash environment, and passes solution/output paths in native Windows form.

Supported stacks:

- .NET -> Stryker.NET (with `--solution` when declared in config)
- Java -> PITest
- JavaScript/TypeScript -> StrykerJS
- Python -> mutmut

#### Step 1a: Analyze-only mode (`--analyze-only`)

Run detection **and** report-file verification in a single call:

```bash
bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh --analyze-only
```

The script outputs all the same detection values as standard mode, plus:

- `analyze_status=ok` + `report_path=<path>` -> a usable report artifact exists
- `report_count=<n>` -> the helper found this many usable raw reports for the selected run
- `manifest_path=<path>` -> when present, this manifest is the authoritative run ledger for multi-report runs
- `analyze_status=no-report` + `analyze_hint=<message>` -> no report found; tell the user to run the mutation tool first (or re-invoke without `--analyze-only`), then stop

When `analyze_status=ok`, **skip Step 2 entirely** and jump to Steps 3–4. For `report_count=1`, you may use `report_path` directly. For `report_count>1`, use `manifest_path` when present or fall back to helper-based discovery under `run_dir`. Read [output-schema.md](./knowledge/output-schema.md) in parallel with the detect call since there is no tool execution to wait for.

### Step 2: Execute the mutation tool _(skipped in analyze-only mode)_

Use the same helper script without `--detect-only`. Omit `--base-branch` unless the user provided an explicit override — the script detects the default branch automatically:

```bash
bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh
```

If the user provided an explicit baseline:

```bash
bash ./.claude/skills/mutation-sentinel/scripts/ms-detect-and-run.sh --base-branch <name>
```

**While the tool runs**, read [output-schema.md](./knowledge/output-schema.md) in parallel so the report contract is loaded before results arrive.

Before starting the expensive run, perform a lightweight capability check for the execution and parsing tools you expect to use:

- `bash`
- `jq` or the platform-native parser you intend to use later
- stack-specific mutation runner (`dotnet-stryker`, `mvn`, `npx stryker`, `mutmut`)

If the preferred parser is missing, select the fallback now and continue. Do not wait until normalization fails.

Execution rules:

- Let the tool output stream to the terminal so failures remain visible; do **not** capture full stdout into context
- Capture the run anchor emitted by the helper before execution begins: `run_started_at_utc`, `expected_report_count`, `helper_strategy`, and `manifest_path`
- When `mode=full` and `expected_report_count` is large, state explicitly that the run may take significant time and that `--analyze-only` is the preferred follow-up path for report refinements
- If the command fails because the tool is missing, report the concrete install path:
  - .NET: `dotnet tool install -g dotnet-stryker`
  - Java: configure PITest in `pom.xml`
  - JS/TS: install `@stryker-mutator/core` and project config
  - Python: `pip install mutmut`
- If the tool fails because the code or tests do not compile or execute, stop and report that mutation testing cannot continue until the baseline test run is healthy
- For long runs, poll completed report count first and read terminal tail only when the helper output and report count disagree or the run appears stalled
- The helper injects `--output StrykerOutput/{project_name}` per project so results land under `StrykerOutput/{project_name}/{timestamp}/reports/`. Do not override this — the output hierarchy is config-driven.

### Steps 3–4: Locate result and normalize (parallel extraction)

In **analyze-only mode**, the helper already told you whether the run is single-report or multi-report:

- if `report_count=1`, use `report_path`
- if `report_count>1` and `manifest_path` exists, read the manifest and use every listed `report_path=` entry
- if `report_count>1` and no manifest exists, fall back to helper-based discovery under `run_dir`

In **standard mode**, locate the raw result using the detected stack:

- .NET -> use the helper manifest when available; otherwise search for `StrykerOutput/*/*/reports/mutation-report.json` under `run_dir`
- Java -> search for `target/pit-reports/*/mutations.xml` under `run_dir`
- JavaScript/TypeScript -> search for `reports/mutation/mutation.json` under `run_dir`
- Python -> use `mutmut results` and `mutmut show <id>` from `run_dir`; there is no canonical JSON or XML contract in the source material

For helper-managed multi-report runs, prefer manifest-driven or shell-driven discovery from `run_dir` over editor glob search. The goal is to normalize the entire run, not the most recent single file.

If the expected file is missing for .NET, Java, or JS/TS:

1. Search the tool's common output folders under `run_dir`
2. Check whether `manifest_path` exists and whether it points to now-missing report files
3. Report what paths you checked
4. Stop rather than guessing at incomplete results

Once located, invoke the checked-in translator script. It owns the normalization and report-writing step for Stryker JSON input and must be preferred over ad hoc shell snippets.

Required invocation shape on Windows:

```powershell
powershell.exe -NoProfile -File ./.claude/skills/mutation-sentinel/scripts/ms-translate-stryker-results.ps1 -ProjectRoot <project-root> -TempRoot <temp-root> -RunId <run-id>
```

If the helper resolved specific report paths or a manifest, prefer those exact inputs instead of rediscovery:

```powershell
powershell.exe -NoProfile -File ./.claude/skills/mutation-sentinel/scripts/ms-translate-stryker-results.ps1 -ProjectRoot <project-root> -TempRoot <temp-root> -ManifestPath <manifest-path>
```

or:

```powershell
powershell.exe -NoProfile -File ./.claude/skills/mutation-sentinel/scripts/ms-translate-stryker-results.ps1 -ProjectRoot <project-root> -TempRoot <temp-root> -ReportPath <report-a>,<report-b>
```

The translator writes these deterministic artifacts under the temp root:

- `ms-survivors.tsv`
- `ms-counts.json`
- `analysis/ms-analysis-<filename-hash>.md`

It also writes `mutation-report-surviving.md` at the repository root unless `-OutputPath` overrides it.

Do **not** read the raw report file into context. All downstream work uses the translator outputs.

#### .NET and JavaScript/TypeScript parsing

Use `./scripts/ms-translate-stryker-results.ps1` as the canonical parser and report generator. Its output must be deterministic in ordering and score formatting, and it must remain checked in with the skill. Manual `jq` or inline PowerShell extraction is only for debugging or when you are actively improving the translator script itself.

The translator emits the same normalized artifacts the rest of this skill expects, so after it runs you should read the temp files and generated markdown report rather than the raw JSON.

#### Java parsing

Parse `mutations.xml` and keep only `<mutation>` nodes with status `SURVIVED` or `NO_COVERAGE`.

Extract:

- source file
- line number
- mutator
- status
- description
- mutated replacement when present in the XML payload

Normalize statuses to the report contract casing:

- `SURVIVED` -> `Survived`
- `NO_COVERAGE` -> `NoCoverage`

#### Python parsing

Run:

```bash
mutmut results
```

For each surviving id, run:

```bash
mutmut show <id>
```

Use that output to populate file, line, mutation type, mutated code, and context. If mutmut does not expose a precise field directly, read the referenced source file around the reported location and make the report explicit about what came from the tool versus what you inferred from the source.

### Step 5: Read source context and explain the gap (parallel by file)

For Stryker runs, the translator already performs deterministic file-grouped source reads and drafts `Why it survived` plus `Suggested fix` into `analysis/ms-analysis-<filename-hash>.md`. Reuse those artifacts unless the user explicitly asked to improve the explanations further or the output is clearly inadequate.

Build a unique-file list from `ms-survivors.tsv`. For each unique source file, launch a **parallel subagent** (max 5 concurrent) that:

1. Reads the full source file once
2. For every mutant in that file: captures original code from the source when the tool does not provide it, drafts `Why it survived` as a test-gap explanation, and drafts `Suggested fix` as a concrete test idea
3. Returns that analysis to the main agent, which writes it to `ms-analysis-<filename-hash>.md`

When there are fewer than 4 unique files, do the reads directly instead of spawning subagents — the overhead is not worth it.

After all subagents complete, read the analysis temp files to assemble the mutant blocks.

Each analysis must follow these quality rules:

Good explanation patterns:

- boundary mutation -> identify the exact boundary value that is missing
- logical connector mutation -> identify the missing independence pair or mixed-boolean case
- arithmetic mutation -> identify concrete operands and the exact result to assert
- no-coverage mutant -> explain that the code path is never executed, then suggest the missing reachability case

Weak explanations to avoid:

- `Tests are missing`
- `The mutant survived because there is no assertion`
- `Add more coverage`

### Step 6: Compute score and quality gate status

Default the target threshold to `80%`.

If the tool configuration clearly exposes a project threshold, prefer that configured value. Otherwise keep `80%`.

Prefer the tool-reported valid-mutant score when the mutation tool exposes it reliably. If you must compute it yourself, use tool-specific denominator rules.

Baseline formula:

```text
(Killed / (Total - Equivalent - CompileError)) * 100
```

Scoring rules:

- Count `Timeout` mutants as killed
- Exclude `CompileError` mutants from the denominator
- Exclude equivalent mutants only when the tool marks them as such or direct evidence shows behavior is unchanged
- For Stryker.NET, also exclude `Ignored` and any other statuses the tool reports as skipped or non-valid for score purposes. Include `Survived` and `NoCoverage` in the denominator and the actionable report.
- If the tool already exposes valid-mutant score semantics, prefer that authoritative value over reconstructing a slightly different denominator
- Include both `Survived` and `NoCoverage` mutants in the actionable report

Set status:

- `PASS` when score >= target
- `FAIL` when score < target

Interpretation guidance:

- `90%+` -> excellent for critical logic
- `75-89%` -> good, but review remaining gaps
- `<50%` -> ineffective tests requiring urgent improvement

### Step 7: Write mutation-report-surviving.md exactly to contract

For Stryker runs, the checked-in translator script is the canonical writer for this step. The agent should verify the generated file and only hand-write or patch the markdown when fixing a translator defect.

Write `./mutation-report-surviving.md` at the **repository root** (`project_root` from Step 1) using [output-schema.md](./knowledge/output-schema.md). This file lives in the repository itself — not in `/tmp/` or any other ephemeral location — so that it is available for downstream skills, CI pipelines, and developers across sessions.

After writing, verify the file exists at the expected path:

```bash
ls -la "${project_root}/mutation-report-surviving.md"
```

Required report content:

- generated timestamp
- project type
- tool name
- score line in the exact contract shape
- target line in the exact contract shape
- status line in the exact contract shape
- summary counts
- one `#### file:line` block per `Survived` or `NoCoverage` mutant
- recommendation section

Ordering rules:

- Sort mutants by file path, then line number, then mutation type
- Keep all survivors from the same file adjacent
- Do not collapse multiple mutants on the same line into a single entry unless they are truly identical mutations

If there are no surviving mutants, still write the report and include:

- `**Status**: PASS`
- an empty-survivor message under `### Surviving Mutants`
- a recommendation that the improvement loop is complete

### Step 8: Close the loop explicitly

When the report status is `FAIL`, tell the user to feed the report into `/test-generator` for targeted tests, then re-run `/mutation-sentinel` (next loop iteration).

When the report status is `PASS`, tell the user the mutation target is met and the improvement loop is complete.

If a previous `mutation-report-surviving.md` existed, mention the score delta from the prior run when you can read it reliably.

## Output Quality Checklist

Before finishing, verify:

- `mutation-report-surviving.md` exists at the repository root (not in `/tmp/` or a subdirectory)
- The skill processed every `Survived` and `NoCoverage` mutant that the tool reported
- The score arithmetic is consistent with the summary counts
- The report matches [output-schema.md](./knowledge/output-schema.md) exactly enough for downstream parsing
- Every `Suggested fix` contains a concrete input or scenario, not generic advice
- The recommendation matches the PASS or FAIL status
- All temp files (`ms-*.tsv`, `ms-*.json`, `ms-analysis-*.md`) were used as working memory only — the final report is in the repo

## Ambiguities to Handle Conservatively

When the source material is incomplete, follow these defaults instead of improvising:

- Multi-module repo -> choose the nearest obvious mutation-test root and state that choice explicitly
- Python mutmut output -> treat CLI output as authoritative because no stable file contract was specified in the source documents
- Equivalent mutants -> only exclude with strong evidence; otherwise keep them actionable or call them likely-equivalent in prose without altering the score

## Troubleshooting Shortlist

- Missing `mutation-targets.json` -> the helper exits with `status=error` and `error=config-not-found`. Create the config file at the repo root with at least one group and one project.
- Invalid config structure -> the helper exits with `error=config-invalid` and a message pointing to the exact missing field (e.g., `groups[0].projects[1].path is required`)
- Missing `jq` on Windows -> the helper falls back to `python3` or PowerShell `ConvertFrom-Json` automatically for config parsing. For Stryker report extraction, use the checked-in PowerShell translator script and continue.
- Temp path mismatch -> create one explicit temp root at the start of the run instead of assuming `/tmp`
- Multi-report .NET run -> use `manifest_path`, `helper_strategy`, and `expected_report_count` rather than assuming one report file. Reports are under `StrykerOutput/{project_name}/{timestamp}/reports/`.
- Analyze-only finds only one report in a historically multi-report repo -> prefer the last-run manifest if it exists; otherwise report the ambiguity and the paths you checked

## Knowledge Index

| File | Purpose |
| --- | --- |
| [output-schema.md](./knowledge/output-schema.md) | Exact markdown contract for `mutation-report-surviving.md` (producer, consumers, template) |
| [mutation-targets-example.json](./knowledge/mutation-targets-example.json) | Annotated example of the `mutation-targets.json` config consumed by the mutation-run helper (groups, projects, per-project configFile overrides) |
