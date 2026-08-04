<!--
  Plan section template — C# impact analysis (LSP).
  Copy the "## Impact Analysis" block below into the plan document, immediately
  after "## Global Constraints" and before the first "### Task N".

  WHEN REQUIRED (CLAUDE.md, writing-plans rule): the plan changes an EXISTING C#
  symbol's signature or behavior — a domain/application method, a shared const,
  an interface, or a public API surface.
  WHEN EXEMPT: new-code-only plans (no existing C# symbol touched), or non-C#
  slices (frontend/docs/infra). In that case still include the section and write
  a single line: "N/A — no existing C# symbol changed." Never delete the heading
  silently — an absent heading reads as "forgot", a present N/A reads as "decided".

  RULE: each changed symbol's blast radius MUST come from the built-in LSP tool
  (findReferences / incomingCalls; goToImplementation for interface or base-type
  changes) — NOT a grep guess. Cite counts and the notable call sites, and
  confirm every caller is covered by a task in this plan.

  POSITION DISCIPLINE: LSP resolves whatever symbol sits at the given
  line:character (both 1-based). Land the offset INSIDE the symbol's own name —
  a near-miss silently resolves the wrong symbol and returns almost nothing,
  which reads exactly like "no callers". Grep the declaration line first, count
  to the name, then query. Record the offset you used, so a reviewer can tell a
  real empty result from a mis-aimed one. If a count looks implausibly small,
  suspect the offset before the codebase.

  CONST EXCEPTION: `public const` values are inlined at compile time, so
  reference queries materially under-report them. Grep const / permission-string
  / enum-literal symbols and write "grep (const — inlined)" in the Tool run
  column. This is the documented exception, not a shortcut.
-->

## Impact Analysis (LSP)

**Method:** built-in `LSP` (`findReferences` / `incomingCalls` / `goToImplementation`), queried at a cited `file:line:char`. Grep is not sufficient here, except for the const case noted below.

> **If `LSP` is unavailable or its results cannot be corroborated** (no server for the file type, workspace not indexed, or a result that contradicts a known call site): say so explicitly in the `Tool run` column (`grep — LSP <reason>`), ground the table with grep as a stopgap, and add to Blast-radius notes: "**Re-run `findReferences` at execution time before editing; add a task for any caller not in this table.**" Record the failure in the DoD ledger too. Honest degradation — never present grep as if it were tool-grounded.

| Changed symbol | Change | Tool run (file:line:char) | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|---------------------------|----------------|--------------------|-----------------|
| `Namespace.Type.Member` | signature \| behavior | `findReferences` @ `File.cs:25:44` | N | `File.cs:line` (module) … | Task N |

**Blast-radius notes:** <cross-module reach, interface implementors, event/handler fan-out, anything the counts above under-state. Note explicitly that the language server is blind to the non-C# stack — TS/JSON couplings such as the permission 5-sync need grep + domain knowledge.>

**Coverage check:** every caller/reference listed above is handled by a task in this plan — <yes, or list the gaps and the tasks added to close them>.
