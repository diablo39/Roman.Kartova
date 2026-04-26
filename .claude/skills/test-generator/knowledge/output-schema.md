# Output Schema: generated-tests-summary.md

**Producer**: `/test-generator`
**Location**: project root as `./generated-tests-summary.md`
**Overwrite behavior**: replace on each invocation
**Related artifacts**: `./.test-gen-audit.csv` (active queue), `./.test-gen-audit-completed.csv` (completed rows)

Write the summary in this structure.

```markdown
# Generated Tests Summary

**Generated**: [ISO 8601 timestamp]
**Mode**: [initial | mutation-killing]
**Input**: [quality-analysis.md | mutation-report-surviving.md]
**Test framework**: [detected framework]
**Test runner**: [command used]

## Statistics

- **Test files created/modified**: [N]
- **Test methods generated**: [N]
- **Source methods covered**: [N]
- **MC/DC independence pairs generated**: [N]
- **Surviving mutants targeted**: [N] (mutation-killing mode only)
- **Equivalent mutants skipped**: [N] (mutation-killing mode only)

## Test Execution Results

- **Tests run**: [N]
- **Passed**: [N]
- **Failed**: [N]
- **Self-repair iterations**: [0-3]
- **Status**: [ALL PASSING | FAILURES REMAIN]

## Generated Tests

### [relative/path/to/test-file.ext]

**Created**: [new | modified]
**Test style**: [example-based | property-based | model-based | mcdc-targeted]

| Test Method | Source Target | Rationale |
|---|---|---|
| [MethodName_Scenario_ExpectedResult] | [source-file:method] | [why this test exists, including business or contract context when that explains the oracle] |
| [MethodName_Scenario_ExpectedResult] | [source-file:method] | [MC/DC or boundary rationale] |
| [MethodName_Scenario_ExpectedResult] | [source-file:method] | [mutant targeted, if applicable] |

### [relative/path/to/another-test-file.ext]

...

## Stale References (if any)

- [analysis or mutation input referenced a missing, moved, or materially changed file]

## Failures (if any)

### [test-method-name]

- **Error**: [compiler, runtime, or assertion error]
- **Repair attempts**: [N of 3]
- **Status**: [resolved | unresolved]
- **Notes**: [what was tried]

When generated tests include assertion-level rationale comments for non-obvious business or contract expectations, summarize that context in the relevant Rationale cell rather than adding extra summary sections.

## Equivalent Mutants Skipped (mutation-killing mode only)

| File:Line | Mutation Type | Original | Mutated | Reason Skipped |
|---|---|---|---|---|
| [file:line] | [type] | [original code] | [mutated code] | [why it appears equivalent] |
```

---

## Input Contract: quality-analysis.md

Read `quality-analysis.md` by headings.

Expected navigation:

1. `## Summary` or equivalent top summary section
2. Risk-group sections ordered HIGH, MEDIUM, LOW
3. `### [file-path]` sections for individual source files
4. `#### [MethodName]` sections for per-method analysis

Extract, when present:

- Cyclomatic complexity
- Compound conditions requiring MC/DC
- Boundary operations with suggested values
- Error-handling paths
- Recommended test focus
- Concrete issues found

Treat missing optional sections as absent data, not as permission to invent findings.

### Compound condition block expectations

Look for content similar to:

```markdown
- Line [N]: `[expression]`
  - Atomic conditions: [A], [B], [C]
  - N = [count], requires [N+1] test cases
  - Independence pairs:
    - [A]: ([pair])
    - [B]: ([pair])
```

### Boundary block expectations

Look for content similar to:

```markdown
- Line [N]: `[expression]` -> test with: [value1], [value2], [value3]
```

---

## Input Contract: mutation-report-surviving.md

Read `mutation-report-surviving.md` by headings.

Expected navigation:

1. Score line or summary
2. Target threshold and pass/fail status when present
3. `#### [filename]:[line number]` sections for individual survivors

Extract, for each survivor:

- Mutation type
- Original code
- Mutated code
- Why it survived

Use this data to design a test that distinguishes original behavior from mutated behavior at the smallest clear divergence point.
