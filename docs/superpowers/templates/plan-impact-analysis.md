<!--
  Plan section template — C# impact analysis (grep).
  Copy the "## Impact Analysis" block below into the plan document, immediately
  after "## Global Constraints" and before the first "### Task N".

  WHEN REQUIRED (CLAUDE.md, writing-plans rule): the plan changes an EXISTING C#
  symbol's signature or behavior — a domain/application method, a shared const,
  an interface, or a public API surface.
  WHEN EXEMPT: new-code-only plans (no existing C# symbol touched), or non-C#
  slices (frontend/docs/infra). In that case still include the section and write
  a single line: "N/A — no existing C# symbol changed." Never delete the heading
  silently — an absent heading reads as "forgot", a present N/A reads as "decided".

  RULE: each changed symbol's blast radius comes from Grep over EVERY access form,
  with each hit read (a hit in a comment or an unrelated same-named member is not a
  caller). Cite the patterns you ran and the hit counts, so a reviewer can re-run them.

  ACCESS FORMS to grep (pick the ones that apply to the symbol kind):
    - method / member call ........ `Name\(`
    - extension method ............ `\.Name\(`
    - method group / delegate ..... `\bName\b` without `(` (e.g. `.Select(Name)`, `MapGet(..., Name)`)
    - nameof / reflection ......... `nameof\(Name\)`, `"Name"`
    - type (ctor, generic arg, DI)  `\bTypeName\b`, `new TypeName\(`, `<TypeName>`
    - interface / base type ....... `: .*\bIName\b` for implementors, then grep each implementor's members
    - const / enum / string value . the const name AND its literal value (the value may be duplicated in
                                     tests, JSON, TS, SQL, or realm files)
  Cross-stack couplings (TS/JSON/SQL/Helm, e.g. the permission 5-sync) are part of the
  blast radius: grep those trees too.
-->

## Impact Analysis

**Method:** Grep over all access forms (see the template header), with every hit read. Cite each pattern + scope so a reviewer can re-run it.

| Changed symbol | Change | Grep patterns (scope) | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|-----------------------|----------------|--------------------|-----------------|
| `Namespace.Type.Member` | signature \| behavior | `Member\(`, `\.Member\(`, `nameof\(Member\)` (`*.cs`) | N | `File.cs:line` (module) … | Task N |

**Blast-radius notes:** <cross-module reach, interface implementors, event/handler fan-out, cross-stack (TS/JSON/SQL) couplings, anything the counts above under-state — e.g. reflection- or convention-based discovery that no textual pattern finds.>

**Coverage check:** every caller/reference listed above is handled by a task in this plan — <yes, or list the gaps and the tasks added to close them>.
