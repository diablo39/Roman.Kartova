# Output Schema: quality-analysis.md

**Producer**: `/quality-analyst` skill
**Consumers**: `/test-generator`, `/mutation-sentinel`, `/coverage-auditor`
**Location**: Project root (`./quality-analysis.md`)
**Overwrite**: Replaced on each invocation

---

## Per-File Subagent Output Format

Each subagent must return its analysis in **exactly** this format. If a section has no items, write the header followed by "None".

```markdown
### [filepath]

**Language**: [language]
**File risk**: [HIGH/MEDIUM/LOW] — [reason]
**Methods in file**: [count]

**Actionable findings**: [count]
**Informational findings**: [count]

**Concrete issues found**:
- [severity] Line [N]: [description] — Evidence: [evidence]

#### [MethodName] `[changed|unchanged|N/A]`

- **Lines**: [start]-[end]
- **Cyclomatic complexity**: [number] [HOTSPOT if > 10]
- **Max nesting depth**: [number]
- **Decision points**:
  - Line [N]: [description]
- **Compound conditions requiring MC/DC**:
  - Line [N]: `[expression]`
    - Atomic conditions: [A], [B], [C]
    - N = [count], requires [N+1] test cases
    - Independence pairs:
      - [A]: ([pair notation])
      - [B]: ([pair notation])
- **Boundary operations**:
  - Line [N]: `[expression]` → test with: [value1], [value2], [value3]
- **Error handling paths**:
  - [description]
- **External dependencies**:
  - [description]
- **Recommended test focus**:
  - [specific recommendation]
```

Repeat the `#### [MethodName]` block for every method in the file. Do not omit any.

---

## Report Structure

```markdown
# Quality Analysis Report

**Generated**: [ISO 8601]
**Mode**: [main | feature-branch]
**Branch**: [branch]
**Scope**: [scope text]
**Total source files**: [from /tmp/qa_classified.txt line count]
**Files deeply analyzed**: [from /tmp/qa_deep_candidates.txt line count]

## Summary

- **Total files**: [N]
- **HIGH risk**: [N] files
- **MEDIUM risk**: [N] files
- **LOW risk**: [N] files
- **Complexity hotspots** (CC > 10): [N] methods
- **Compound conditions requiring MC/DC**: [N] conditions across [N] files
- **Critical issues found**: [N]

## Scan Manifest

- **Deep analysis buckets**: [from /tmp/qa_deep_candidates.txt bucket column]
- **Generated code excluded**: [from /tmp/qa_excluded_generated.txt line count] files
- **DTO/model files excluded**: [from /tmp/qa_classified.txt grep dto-model count] files
- **Selection rationale**: [from /tmp/qa_summary.txt]

---

## Critical Issues Found

| # | File | Line | Issue | Severity | Evidence |
|---|------|------|-------|----------|----------|

---

## HIGH Risk Files

### [relative/path]
[subagent output]

---

## MEDIUM Risk Files

### [relative/path]
[subagent output]

---

## LOW Risk Files

### [relative/path]
[subagent output]

---

## Common Anti-Patterns

1. [pattern] — found in [N] files

## Coverage Gaps and Confidence

- [what was deeply analyzed vs heuristically classified]
- [where confidence is high]
- [where confidence is limited]

## Benchmark Metrics

| Metric | Value |
|--------|-------|
| Finding density | [findings / files analyzed] |
| Method coverage ratio | [methods / unique files] |
| Actionable findings ratio | [(bugs + security) / total findings] |
| Max CC detected | [value] |
| Average CC of hotspots | [value] |
| Security surface coverage | [covered / known] |
| Nesting depth >=3 | [count] |
| Nesting depth >=4 | [count] |
| Nesting depth >=5 | [count] |
| External dependency count | [count] |
| Recommended test case count | [sum MC/DC N+1] |
```

## Parsing Contract for Consumers

Downstream agents parse this document using these rules:

| To find... | Parse rule |
|------------|-----------|
| All analyzed files | `### ` headings under `## HIGH/MEDIUM/LOW Risk Files` |
| File risk level | Which `## Risk` section the file appears under |
| Methods in a file | `#### ` headings under a file's `### ` heading |
| Method change status | Text in backticks after method name: `changed`, `unchanged`, or `N/A` |
| Complexity score | Line starting with `- **Cyclomatic complexity**:` |
| Compound conditions | Lines under `**Compound conditions requiring MC/DC**` |
| Independence pairs | Indented lines under compound condition with condition name and pair notation |
| Boundary values | Lines under `**Boundary operations**:` with `→ test with:` |
| Test recommendations | Bullet list under `**Recommended test focus**:` |