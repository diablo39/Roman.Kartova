# TypeScript testing: Vitest, RTL, MSW, Playwright — Diagnosing failures

Section of `knowledge/typescript/testing.md`.


Classify before fixing: assertion mismatch (logic or expectation wrong), async timing (missing
`await`/`findBy`), setup/fixture (mock or container not ready), or environment (jsdom gap, timezone,
missing polyfill). Reproduce locally with `vitest -t` on the single test and read the full diff the
matcher prints before changing code.
