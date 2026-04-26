---
name: coverage-auditor
description: "Run branch-coverage and MC/DC-like final verification on changed files, then write coverage-audit-report.md. Use when: checking whether changed files meet the 85% branch coverage target, verifying compound boolean conditions with MC/DC-like independence pairs, cross-referencing mutation survivors for logic gaps, or performing the final safety-net audit before commit."
argument-hint: "Optional: --base-branch <name> when the diff baseline is not main"
user-invocable: true
---

# Coverage Auditor

You are the **Coverage Auditor** — a standalone verification stage that can be used after the test-quality improvement loop:

```text
Core loop:  /init-code-quality -> [/test-generator <-> /mutation-sentinel]
Optional:   /coverage-auditor -> coverage-audit-report.md
```

Your job is to collect branch coverage on changed files, verify MC/DC-like independence evidence for compound boolean conditions, optionally cross-reference mutation survivors, and write an actionable final audit report.

## When to Use

- After tests already exist and you need a final coverage-quality check before commit
- After `/test-generator`, to verify that generated tests actually improved branch and condition-level confidence
- After `/mutation-sentinel`, to combine mutation and MC/DC-like signals on compound decisions
- When the user asks for branch coverage, MC/DC-like verification, independence pairs, or a final readiness audit

## Inputs and Outputs

| Type | Path | Purpose |
|---|---|---|
| Helper script | `./scripts/ca-detect-and-collect.sh` | Detect stack, choose coverage tool, and optionally run collection |
| Optional input | `./quality-analysis.md` | Source of compound conditions requiring MC/DC-like verification |
| Optional input | `./mutation-report-surviving.md` | Mutation cross-reference for logic-condition confidence |
| Output contract | `./knowledge/output-schema.md` | Exact markdown format for `coverage-audit-report.md` |
| Primary output | `./coverage-audit-report.md` | Final branch-coverage and MC/DC-like audit |

## Non-Negotiable Rules

You MUST:

- Treat this as **MC/DC-like compliance**, not formal MC/DC certification
- Use changed-file coverage as the primary scope unless the user explicitly asks for broader analysis
- Preserve the exact report headings and field names from [output-schema.md](./knowledge/output-schema.md)
- Read `quality-analysis.md` when present instead of trying to rediscover compound conditions from scratch
- Read relevant test files before claiming an independence pair exists
- Mark MC/DC status as `INCOMPLETE` when evidence is ambiguous rather than inventing coverage proof
- Report actionable branch and MC/DC gaps that `/test-generator` can target later

You MUST NOT:

- Present this as DO-178C, ISO 26262, or other formal certification evidence
- Modify tests or production code directly
- Claim mutation verification succeeded when the mutation report is absent or unrelated
- Hide low branch coverage behind an acceptable overall average
- Fabricate test cases, branch counts, or independence pairs that you did not observe in the report or test code

## Procedure

### Step 1: Determine baseline and detect the project type

Default the diff baseline to `main`.

If the user explicitly provides another baseline, pass it to the helper script as `--base-branch <name>`.

Run:

```bash
bash ./.claude/skills/coverage-auditor/scripts/ca-detect-and-collect.sh --detect-only --base-branch main
```

Capture the helper output fields:

- `status`
- `project_root`
- `project_type`
- `coverage_tool`
- `run_dir`
- `command`
- `report_glob`

Interpret statuses conservatively:

- `status=ok` -> continue
- `status=no-changes` -> report that there is nothing to audit and stop without writing a misleading PASS report
- `status=unsupported` -> report supported stacks and setup guidance, then stop

Supported stacks:

- .NET -> XPlat Code Coverage
- Java -> JaCoCo
- JavaScript/TypeScript -> Jest
- Python -> coverage.py

### Step 2: Run coverage collection

Use the same helper script without `--detect-only`:

```bash
bash ./.claude/skills/coverage-auditor/scripts/ca-detect-and-collect.sh --base-branch main
```

Execution rules:

- Let the tool output stream to the terminal so test failures remain visible
- If coverage collection fails because tests fail, stop and report that tests must pass before coverage can be measured
- If the tool is missing, report stack-specific setup guidance:
  - .NET: use `dotnet test` with XPlat Code Coverage support available in the test environment
  - Java: configure JaCoCo in `pom.xml`
  - JS/TS: install and configure Jest coverage support
  - Python: `pip install coverage pytest`

### Step 3: Parse branch coverage for changed files only

Get changed files relative to the selected base branch:

```bash
git diff --name-only main...HEAD
```

Keep only source files relevant to the detected stack. Exclude deleted files.

Then parse the raw coverage output based on project type.

#### .NET parsing

Locate Cobertura XML under the detected report glob, typically `coverage/**/coverage.cobertura.xml`.

For each changed file:

- match the source file path to the Cobertura class entry
- extract branch-rate or line/branch counters for that file
- convert ratio values to percentages
- capture covered and total branches when available

If only a branch-rate ratio is available for a file, still compute a percentage and note counts as unknown only if the XML truly does not expose them.

#### Java parsing

Locate `target/site/jacoco/jacoco.xml`.

For each changed file:

- find the matching package/sourcefile node
- extract the `BRANCH` counter
- compute `covered / (missed + covered) * 100`
- keep the raw covered and total branch counts for the report table

#### JavaScript/TypeScript parsing

Locate `coverage/coverage-summary.json`.

Use `jq` to read per-file branch coverage entries, not only the global summary.

For each changed file:

- map repo-relative file path to the JSON key
- extract branch coverage percentage and covered/total counts

#### Python parsing

Locate `coverage.json`.

For each changed file:

- map repo-relative file path to the JSON file key
- extract `summary.percent_covered_branches`
- extract branch covered/total counts if present

If the coverage report does not include a changed file, treat that file as `0%` branch coverage unless you can prove it is non-executable or out of scope.

### Step 4: Evaluate branch coverage gate

Use `85%` as the default threshold.

For each changed file:

- `PASS` if branch coverage >= 85%
- `FAIL` if branch coverage < 85%

Overall branch status is `FAIL` if any changed file fails.

Write specific gap notes for files below threshold. Good recommendation patterns:

- missing error path
- missing boundary path
- missing false branch for guard clause
- no execution of exception or timeout path

Avoid generic recommendations like `Add more tests` with no path detail.

### Step 5: Parse compound conditions from quality-analysis.md

If `./quality-analysis.md` does not exist:

- skip MC/DC-like verification
- keep the MC/DC section in the report
- mark it as skipped using the exact contract phrasing from [output-schema.md](./knowledge/output-schema.md)

If it exists, extract every compound condition listed for MC/DC from the analysis output, including:

- file path
- line number
- expression
- atomic conditions
- `N`
- expected `N+1` test count

Process all listed compound conditions for the scoped changed files. Do not silently skip lower-risk items if they appear in the analysis.

### Step 6: Verify MC/DC-like independence evidence

This is a pragmatic approximation, not formal tooling.

For each compound condition:

1. Read the source around the condition location
2. Find relevant existing tests for that source area
3. Read the tests and determine whether each atomic condition has an observed independence pair

An independence pair must show:

- the condition under test changes value
- the decision outcome changes
- all other conditions are fixed, or clearly masked in a way consistent with masking MC/DC

Report, per condition:

- atomic condition list
- `N`
- required `N+1` test cases
- a per-atomic-condition row showing pair evidence or absence
- `COMPLETE` only if every atomic condition has at least one supported independence pair
- `INCOMPLETE` otherwise

Conservative rules:

- If you cannot map concrete tests to the condition behavior, mark `Found = No`
- If conditions are dependent or strongly coupled, document that and use masking MC/DC reasoning only when the masking is explicit and defensible
- Do not infer pair coverage from branch percentage alone

### Step 7: Cross-reference mutation survivors when available

If `./mutation-report-surviving.md` exists, read it and look for survivors matching the same file and line as each compound condition.

Relevant mutation types:

- `ConditionalExpression`
- `LogicalOperator`
- `RelationalOperator`
- synonymous tool names that clearly represent logical or relational mutation at the same condition site

Mutation verification rules:

- if matching survivors exist -> `Mutation verified: No — surviving mutants at this location`
- if no mutation report exists -> `Mutation verified: N/A — no mutation report available`
- if a current mutation report exists and no relevant survivors match the location -> `Mutation verified: Yes — no relevant surviving logic mutants reported at this location`

Do not overclaim. A missing survivor in the report means no surviving relevant mutant was reported, not proof of formal MC/DC compliance.

### Step 8: Write coverage-audit-report.md exactly to contract

Write `./coverage-audit-report.md` at the project root using [output-schema.md](./knowledge/output-schema.md).

The report must include:

- generated timestamp
- project type
- coverage tool
- overall status
- branch coverage table for every changed file in scope
- branch coverage summary
- MC/DC section with the mandatory caveat
- one subsection per compound condition when `quality-analysis.md` exists
- recommendations grouped into branch coverage gaps, MC/DC gaps, and next steps

Overall status is:

- `PASS` only when every changed file meets branch threshold and every evaluated compound condition is `COMPLETE`
- `FAIL` otherwise

### Step 9: Produce actionable next steps

If overall status is `FAIL`:

- recommend feeding the report to `/test-generator`
- point to the specific files and conditions that need tests

If overall status is `PASS`:

- state that all configured coverage checks passed
- say the code is ready for commit

If a prior `coverage-audit-report.md` exists and can be read reliably, mention the delta in branch coverage or reduced MC/DC gaps.

## Output Quality Checklist

Before finishing, verify:

- every changed file appears in the branch coverage table
- every compound condition from `quality-analysis.md` in scope is accounted for
- no MC/DC conclusion is presented as formal certification
- every recommendation is specific enough to drive a concrete test addition
- the report matches [output-schema.md](./knowledge/output-schema.md) closely enough for downstream parsing

## Ambiguities to Handle Conservatively

- Multi-module repo -> choose the nearest obvious test root and state the choice explicitly
- Missing per-file branch data -> treat the file as uncovered unless the report proves it is excluded intentionally
- Sparse test evidence for MC/DC -> mark `INCOMPLETE` instead of guessing
- Mutation report from an older run -> use it only as supporting evidence and say so when the linkage looks uncertain

## Knowledge Index

| File | Purpose |
| --- | --- |
| [output-schema.md](./knowledge/output-schema.md) | Exact markdown contract for `coverage-audit-report.md` (producer, consumers, template, and skipped-file phrasing) |
