---
description: "Analyzes source code for complexity hotspots, decision points, compound boolean conditions, concrete bugs, and produces quality-analysis.md. Uses deterministic bash-based file discovery. Excludes all generated code and DTOs."
---

## User Input

```text
$ARGUMENTS
```

## Instructions

Follow `.claude/skills/quality-analyst/SKILL.md` as the authoritative workflow.

Key requirements:
1. **Run the bash script first** — file discovery is deterministic, not LLM-driven
2. **Only analyze files from `/tmp/qa_deep_candidates.txt`** — generated code and DTOs are already excluded
3. **Use the analysis checklist** from `./skills/quality-analyst/references/analysis-checklist.md` for each subagent
4. **Use the output schema** from `./skills/quality-analyst/references/output-schema.md` for report structure
5. **Every method** in deep-analyzed files gets full analysis — do not skip any