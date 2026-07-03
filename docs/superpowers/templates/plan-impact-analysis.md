<!--
  Plan section template — C# impact analysis (codelens/LSP).
  Copy the "## Impact Analysis" block below into the plan document, immediately
  after "## Global Constraints" and before the first "### Task N".

  WHEN REQUIRED (CLAUDE.md, writing-plans rule): the plan changes an EXISTING C#
  symbol's signature or behavior — a domain/application method, a shared const,
  an interface, or a public API surface.
  WHEN EXEMPT: new-code-only plans (no existing C# symbol touched), or non-C#
  slices (frontend/docs/infra). In that case still include the section and write
  a single line: "N/A — no existing C# symbol changed." Never delete the heading
  silently — an absent heading reads as "forgot", a present N/A reads as "decided".

  RULE: each changed symbol's blast radius MUST come from roslyn-codelens
  (find_callers / find_references / analyze_change_impact; find_implementations
  / get_type_hierarchy for interface or base-type changes) or the built-in LSP
  equivalents — NOT a grep guess. Cite counts and the notable call sites, and
  confirm every caller is covered by a task in this plan.
-->

## Impact Analysis (codelens/LSP)

**Method:** roslyn-codelens (`find_callers` / `find_references` / `analyze_change_impact`) · built-in `LSP` fallback. Grep is not sufficient here.

> **If codelens/LSP is unavailable this session** (e.g. MCP not loaded — headless `claude -p` does not start project MCP servers): say so explicitly in the `Tool run` column (`grep — codelens unavailable`), ground the table with grep as a stopgap, and add to Blast-radius notes: "**Re-run `find_callers`/`find_references` at execution time before editing; add a task for any caller not in this table.**" Honest degradation — never present grep as if it were codelens.

| Changed symbol | Change | Tool run | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|----------|----------------|--------------------|-----------------|
| `Namespace.Type.Member` | signature \| behavior | `find_callers` | N | `File.cs:line` (module) … | Task N |

**Blast-radius notes:** <cross-module reach, interface implementors, event/handler fan-out, anything the counts above under-state>

**Coverage check:** every caller/reference listed above is handled by a task in this plan — <yes, or list the gaps and the tasks added to close them>.
