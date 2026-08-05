# TypeScript testing: Vitest, RTL, MSW, Playwright — Anti-patterns and flakiness

Section of `knowledge/typescript/testing.md`.


- Fixed `setTimeout`/`waitForTimeout` — the top cause of flaky tests. Wait for a condition
  (`findBy*`, `waitFor`, auto-waiting assertions).
- `getByTestId` where an accessible query exists; asserting on class names or DOM structure.
- Snapshot tests over large trees — they rubber-stamp changes and rot. Snapshot small, stable output
  only, and review every update.
- Mocking the module under test, or asserting a mock was called instead of asserting the effect.
- Shared mutable state between tests, order-dependent suites, and unawaited async in the test body.
