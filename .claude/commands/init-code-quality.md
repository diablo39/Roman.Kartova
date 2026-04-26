---
description: "Initialize .NET mutation testing and coverage setup. Bootstraps Stryker.NET and Coverlet configuration with conservative defaults."
---

## User Input

```text
$ARGUMENTS
```

## Instructions

Follow `.claude/skills/init-code-quality/SKILL.md` as the authoritative workflow.

Key requirements:
1. **Inspect before changing** — detect existing Stryker and Coverlet assets first
2. **Preserve existing config** — never overwrite `stryker-config.json`, `.runsettings`, or package references without user approval
3. **Check NuGet auth** — verify authentication before any restore or package install when private feeds are present
4. **Use bundled templates** — copy from `./skills/init-code-quality/assets/` only when the relevant config is missing
5. **Validate narrowly** — confirm tool installation and package references after changes
