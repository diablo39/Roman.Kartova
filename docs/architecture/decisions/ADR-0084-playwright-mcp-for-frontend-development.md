# ADR-0084: Playwright MCP for Frontend Development and Verification Workflow

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Development Workflow
**Related:** ADR-0039 (React SPA + TypeScript), ADR-0083 (testing strategy — Playwright as E2E framework), ADR-0079 (dogfooding GTM), ADR-0087 (Google Stitch MCP as design source — full loop: Stitch → code → Playwright)

## Context

Kartova is built by a solo developer with heavy AI-assisted workflow. When developing or changing frontend features, the AI assistant needs a way to:

- Open the running dev server in a real browser
- Navigate, click, fill forms, observe actual rendering
- Capture screenshots and DOM snapshots for verification
- Inspect console errors and network requests
- Detect visual regressions before committing

Without browser automation the AI is blind to the running UI — it can only reason about code, not confirm that the rendered result matches intent. This is a known gap in AI-assisted frontend work: "type-checking and tests verify code correctness, not feature correctness."

A separate decision from ADR-0083: that ADR covers **Playwright as CI test framework** (authored test files, assertions, CI execution). This ADR covers **Playwright MCP as live development tool** during the coding loop.

## Decision

Use the **Playwright MCP server** as the mandatory browser-automation tool during frontend development and verification with AI assistance. The MCP server is already registered in this project (project-scope `.mcp.json`).

**Workflow contract:**

1. Before claiming a frontend change is complete, the assistant must exercise the feature in a real browser via Playwright MCP — not just trust the TypeScript compiler or unit tests.
2. The assistant should test the golden path plus at least one edge case for every user-facing change (per user preference from CLAUDE.md working agreements).
3. Console errors observed via MCP must be resolved before declaring the task done, or explicitly flagged to the user if intentionally deferred.
4. If the dev server is not running, the assistant starts it first; if the browser is unavailable (MCP not loaded, headless mode issue), the assistant states so explicitly rather than silently skipping verification.

**Distinction from ADR-0083 Playwright E2E tests:**

| Concern | ADR-0083 (E2E test tier) | ADR-0084 (this) |
|---------|--------------------------|-----------------|
| Purpose | Automated regression tests in CI | Live verification during development |
| When run | On PR / main branch | Interactively during coding session |
| Authored by | Developer, committed to repo | AI assistant on-the-fly via MCP |
| Output | Test pass/fail, coverage | Screenshots, DOM snapshots, console logs |
| Persistence | Test files in `tests/Kartova.E2E/` | Ephemeral; artifacts discarded or attached to PR description |

## Rationale

- **Closes the "code looks right but renders wrong" gap** — TypeScript + unit tests do not verify CSS, layout, keyboard focus, or runtime errors.
- **Matches dogfooding posture (ADR-0079)** — every AI-driven change should be self-verified before the developer looks at it.
- **Cheap and fast** — MCP overhead is negligible compared to writing full E2E tests for every small change; use of MCP is complementary, not a substitute.
- **Already registered** — Playwright MCP server is configured at project scope (`.mcp.json`); no additional setup.
- **Consistent with CLAUDE.md working agreement** "for UI or frontend changes, start the dev server and use the feature in a browser before reporting the task as complete."

## Alternatives Considered

- **Trust TypeScript + unit tests only** — rejected; misses runtime, layout, CSS, accessibility, console-error regressions.
- **Ask the developer to verify manually** — breaks the AI-assisted flow; the assistant should deliver already-verified work.
- **Puppeteer MCP or Selenium** — Playwright's MCP server is actively maintained, first-class in the Claude Code / Anthropic ecosystem, and aligns with ADR-0083 (single browser-automation stack across dev + CI).
- **Visual regression tools (Percy, Chromatic)** — complementary, not a replacement; may be added post-MVP.

## Consequences

**Positive:**
- Frontend changes are verified in a real browser before claimed complete
- Console errors and layout issues caught immediately
- Screenshots in PR descriptions document the change visually
- Same underlying Playwright investment powers both dev (MCP) and CI (tests)

**Negative / Trade-offs:**
- MCP must be loaded in the Claude Code session — requires restart after `.mcp.json` changes
- API keys in MCP config are sensitive — `.mcp.json` is gitignored, but headers still appear in Claude Code logs
- Headless browser requires the dev server to be running — adds a prerequisite to the inner dev loop

**Neutral:**
- Does not replace the need for authored E2E tests in ADR-0083 — MCP is exploratory, E2E tests are regression safety net
- Not applicable to backend-only work — optional when there is no UI surface

## Implementation Notes

- Playwright MCP already registered in project (`.mcp.json`)
- During frontend work, expected commands: `browser_navigate`, `browser_click`, `browser_fill_form`, `browser_snapshot`, `browser_take_screenshot`, `browser_console_messages`, `browser_network_requests`
- Dev server script (to be defined in Phase 0 scaffolding): `npm run dev` inside `src/web` starts Vite on `http://localhost:5173`
- Verification checklist (per frontend change):
  1. `browser_navigate` to affected page
  2. Exercise the feature (click / fill / submit)
  3. `browser_console_messages` — ensure no errors
  4. `browser_snapshot` or `browser_take_screenshot` for visual evidence
  5. Include screenshot in PR description (manual paste)

## References

- Playwright MCP server: https://github.com/microsoft/playwright-mcp
- Playwright docs: https://playwright.dev/
- ADR-0039 (React SPA), ADR-0083 (Playwright E2E tier), ADR-0079 (dogfooding)
- CLAUDE.md working agreement: "For UI or frontend changes, start the dev server and use the feature in a browser before reporting the task as complete."
