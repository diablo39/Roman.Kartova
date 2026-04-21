# ADR-0087: Google Stitch MCP as Design Source for Frontend Implementation

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Development Workflow
**Related:** ADR-0039 (React SPA), ADR-0084 (Playwright MCP verification), ADR-0083 (testing strategy), ADR-0088 (component stack — navigation canonicalized in DESIGN.md, not Stitch, due to inconsistent Stitch nav generations)

## Context

Kartova's UI screens were designed in Google Stitch and documented in `docs/design/STITCH-PROMPTS.md` and `docs/design/DESIGN.md` (tokens, nav specs). Google Stitch MCP server is registered at project scope (`.mcp.json`) and provides programmatic access to the generated mockups — screens, components, DOM structure, design tokens, and visual artifacts — without leaving the coding session.

Without explicit guidance, an AI assistant implementing a frontend feature may reconstruct the UI from memory or from the text prompts alone, losing fidelity to the actual approved design.

## Decision

Use the **locally-committed Stitch output in `docs/ui-screens/` as the primary design-source reference**, with **Google Stitch MCP as escalation** when local files are missing, stale, or under live iteration. Before writing component code for a screen:

1. **Default — read local files:**
   - `docs/ui-screens/{screen-name}/code.html` (Stitch-generated HTML + Tailwind classes — canonical structure)
   - `docs/ui-screens/{screen-name}/screen.png` (visual reference)
2. **Escalate to Stitch MCP** when:
   - The target screen is not present in `docs/ui-screens/`
   - The user explicitly asks to sync / refresh from Stitch
   - Local files appear stale or diverge from what the user describes
3. Extract the visual structure, spacing, tokens, and interactive elements
4. Align the implementation to the mockup — layout, component hierarchy, states, edge cases
5. Verify the rendered result with Playwright MCP (ADR-0084)

**Rationale for local-first:** `code.html` contains the full DOM hierarchy and Tailwind utility classes Stitch generated. Mapping to shadcn/ui (ADR-0088) becomes mechanical rather than reconstructive. Committed files are a canonical, diff-able, reproducible snapshot — any AI session or CI step produces the same output. MCP adds latency and can disconnect mid-session.

**Full frontend loop:**

```
docs/ui-screens/{screen}/ (local)  ──┐
   or Stitch MCP (if missing)      ──┴──► implementation → Playwright MCP (verify) → commit
```

Deviations from the mockup require either a regeneration (update the local files) or an explicit note in the PR description. When local files are updated from Stitch, commit the refreshed `code.html` + `screen.png` together.

## Rationale

- **Prevents design drift** — every frontend change is grounded in the approved design, not reconstructed from memory
- **Matches design system discipline** (ADR-0039, DESIGN.md) — committed Stitch snapshot is canonical; prompts in STITCH-PROMPTS.md are only reference
- **Complements ADR-0084** — Stitch defines "what it should look like", Playwright verifies "what it actually looks like"
- **Local-first is faster and reproducible** — `code.html` + `screen.png` read in milliseconds; no MCP round-trip, no connection flakiness; diff-able in git; same input on every session
- **MCP remains available** — for missing screens, live iteration, or user-requested sync; best of both worlds
- **Reduces back-and-forth** — fewer rounds of "no, that's not what the mockup showed" between AI and developer

## Alternatives Considered

- **Rely on STITCH-PROMPTS.md text only** — loses visual information, tokens, hierarchy. Rejected: prompts describe, mockups specify.
- **Figma / other design tools** — Stitch is already the chosen design tool (prior decision); switching adds work without value
- **Screenshots pasted into PR** — manual, stale, out of coding-session context. MCP is strictly better.

## Consequences

**Positive:**
- Implementation matches design with less review iteration
- Design tokens (DESIGN.md) stay authoritative — Stitch output references them
- Combined with ADR-0084, frontend changes are both visually-accurate and runtime-verified before commit

**Negative / Trade-offs:**
- MCP must be loaded and authenticated — session restart after `.mcp.json` changes
- Stitch API access depends on Google's availability (not usually an issue)
- If mockup and implementation diverge intentionally (e.g., developer changed mind), Stitch must be re-generated or decision documented in PR

**Neutral:**
- Does not apply to backend-only work
- For pages not yet designed in Stitch, fall back to STITCH-PROMPTS.md and DESIGN.md tokens

## Implementation Notes

- Stitch MCP registered in `.mcp.json` (gitignored per project policy); used as escalation, not default
- `docs/ui-screens/` layout: one folder per screen, containing `code.html` + `screen.png`
- Expected usage (local-first):
  1. Read `docs/ui-screens/{screen-name}/code.html` for DOM + Tailwind structure
  2. Read `docs/ui-screens/{screen-name}/screen.png` for visual confirmation
  3. Map Stitch HTML → shadcn/ui components (ADR-0088)
  4. Apply DESIGN.md tokens where Stitch output uses hardcoded values
  5. Verify with Playwright MCP per ADR-0084
- Expected usage (Stitch MCP escalation):
  1. Screen missing from `docs/ui-screens/` → query Stitch MCP for the mockup
  2. If accepted, save the returned HTML + screenshot into `docs/ui-screens/{screen-name}/` and commit — next iteration goes back to local-first
- If a screen is not in Stitch at all → generate it there first, then commit to `docs/ui-screens/`

## References

- Google Stitch: https://stitch.google.com (authenticated MCP endpoint)
- `docs/design/STITCH-PROMPTS.md` (screen prompts)
- `docs/design/DESIGN.md` (design tokens, navigation specs)
- ADR-0039 (React SPA), ADR-0084 (Playwright MCP), ADR-0083 (testing)
