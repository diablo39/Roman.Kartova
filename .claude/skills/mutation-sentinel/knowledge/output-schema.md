# Output Schema: mutation-report-surviving.md

**Producer**: `/mutation-sentinel` skill
**Consumers**: Developer review, `/test-generator`, `/test-generator-gh`, pipeline orchestration
**Location**: Project root (`./mutation-report-surviving.md`)
**Overwrite**: Replaced on each invocation

---

## Template

Write the report in exactly this format:

```markdown
## Mutation Testing Results

**Generated**: [ISO 8601 timestamp]
**Project type**: [.NET | Java | JavaScript/TypeScript | Python]
**Tool**: [Stryker.NET | PITest | StrykerJS | mutmut]
**Score**: X% (Y killed / Z total)
**Target**: >= 80%
**Status**: [PASS|FAIL]

### Summary

- **Total mutants**: [N]
- **Killed**: [N]
- **Survived**: [N]
- **No coverage**: [N]
- **Equivalent (excluded)**: [N]
- **Compile errors (excluded)**: [N]
- **Timeouts (counted as killed)**: [N]

### Surviving Mutants

#### [filename]:[line number]
- **Mutation type**: [e.g., ConditionalBoundary, ArithmeticOperator, LogicalConnector]
- **Mutant status**: [Survived | NoCoverage]
- **Original code**: `[original source expression]`
- **Mutated code**: `[mutated expression]`
- **Why it survived**: [Human-readable explanation of the test gap]
- **Suggested fix**: [Specific test case description to kill this mutant]

#### [filename]:[line number]
...

### Recommendation

[If FAIL]: Feed this report to `/test-generator` to generate targeted tests for the surviving mutants. Expected improvement: 10-25 percentage points per feedback iteration.

[If PASS]: Mutation score meets the target threshold. The improvement loop is complete.
```

---

## Parsing Contract for Consumers

Downstream agents parse this document using these rules:

| To find... | Parse rule |
|---|---|
| Mutation score | Line matching `**Score**: X% (Y killed / Z total)` |
| Target threshold | Line matching `**Target**: >= N%` |
| Pass/fail status | Line matching `**Status**: [PASS|FAIL]` |
| All surviving mutants | `#### ` headings under `### Surviving Mutants` |
| Mutant file and line | Heading text: `[filename]:[line number]` |
| Mutation type | Line starting with `- **Mutation type**:` |
| Original code | Line starting with `- **Original code**:` |
| Mutated code | Line starting with `- **Mutated code**:` |
| Survival reason | Line starting with `- **Why it survived**:` |
| Suggested fix | Line starting with `- **Suggested fix**:` |
| Mutant status | Line starting with `- **Mutant status**:` |

---

## Compatibility Note

This schema is intentionally compatible with the existing mutation-killing workflow. Do not rename headings or field labels unless the downstream consumers are updated in the same change.
