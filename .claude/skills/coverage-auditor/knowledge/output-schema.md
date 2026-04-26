# Output Schema: coverage-audit-report.md

**Producer**: `/coverage-auditor` skill
**Consumers**: Developer review, `/test-generator`, `/test-generator-gh`, pipeline orchestration
**Location**: Project root (`./coverage-audit-report.md`)
**Overwrite**: Replaced on each invocation

---

## Template

Write the report in exactly this format:

```markdown
# Coverage Audit Report

**Generated**: [ISO 8601 timestamp]
**Project type**: [.NET | Java | JavaScript/TypeScript | Python]
**Coverage tool**: [XPlat Code Coverage | JaCoCo | Jest | coverage.py]
**Overall status**: [PASS|FAIL]

## Branch Coverage

**Threshold**: >= 85%

| File | Branch Coverage | Branches (covered/total) | Status |
|------|----------------|--------------------------|--------|
| [relative/path/to/file] | [XX.X%] | [N/M] | [PASS|FAIL] |
| [relative/path/to/file] | [XX.X%] | [N/M] | [PASS|FAIL] |

**Branch coverage summary**: [N] of [M] changed files meet the 85% threshold.

## MC/DC Independence Pair Verification

**Note**: This is MC/DC-like compliance — a pragmatic approximation, not formal MC/DC certification (which requires specialized tooling like LDRA or Qt Coco).

**Source**: quality-analysis.md
[If quality-analysis.md not found: "Skipped — quality-analysis.md not found. Run `/quality-analyst` first for compound condition analysis."]

### [file:line] `[expression]`

**Atomic conditions**: [A], [B], [C] (N = [count], requires [N+1] test cases)
**Status**: [COMPLETE|INCOMPLETE]
**Mutation verified**: [Yes — no relevant surviving logic mutants reported at this location | No — surviving mutants at this location | N/A — no mutation report available]

**Independence pairs**:

| Condition | Test Case 1 | Test Case 2 | Decision Change | Found |
|-----------|-------------|-------------|-----------------|-------|
| [A] | [description: A=T, B=T, C=T -> TRUE] | [description: A=F, B=T, C=T -> FALSE] | TRUE -> FALSE | [Yes|No] |
| [B] | [description: A=T, B=T, C=T -> TRUE] | [description: A=T, B=F, C=T -> FALSE] | TRUE -> FALSE | [Yes|No] |
| [C] | [description: A=T, B=T, C=T -> TRUE] | [description: A=T, B=T, C=F -> FALSE] | TRUE -> FALSE | [Yes|No] |

**Missing pairs**: [List any conditions without found independence pairs, or "None"]

### [file:line] `[expression]`
...

## Recommendations

### Branch Coverage Gaps

- [file]: Branch coverage is [XX%], below 85% threshold. [Specific recommendation, e.g., "Add tests for the error handling path at line N"]

### MC/DC Gaps

- [file:line] `[expression]`: Condition [X] lacks an independence pair. [Specific recommendation, e.g., "Add a test case where X is false while Y and Z are true, expecting the decision to be FALSE"]

### Next Steps

[If FAIL]: Feed this report to `/test-generator` to generate targeted tests for the identified gaps.

[If PASS]: All coverage checks pass. Code is ready for commit.
```

---

## Parsing Contract for Consumers

Downstream agents parse this document using these rules:

| To find... | Parse rule |
|---|---|
| Overall status | Line matching `**Overall status**: [PASS|FAIL]` |
| Branch coverage per file | Table rows under `## Branch Coverage` |
| File path | First column of branch coverage table |
| Branch coverage percentage | Second column of branch coverage table |
| File status | Fourth column of branch coverage table |
| MC/DC conditions | `### ` headings under `## MC/DC Independence Pair Verification` |
| Condition expression | Backtick-wrapped text in MC/DC heading |
| MC/DC status | Line matching `**Status**: [COMPLETE|INCOMPLETE]` |
| Missing pairs | Line starting with `**Missing pairs**:` |
| Specific recommendations | Bullet items under `### Branch Coverage Gaps` and `### MC/DC Gaps` |

---

## Compatibility Note

This schema is intentionally structured so a downstream test-generation skill can target both branch-coverage gaps and missing independence pairs without reparsing raw tool reports.
