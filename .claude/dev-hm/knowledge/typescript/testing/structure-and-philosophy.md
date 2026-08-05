# TypeScript testing: Vitest, RTL, MSW, Playwright — Structure and philosophy

Section of `knowledge/typescript/testing.md`.


- Arrange–Act–Assert: set up state, perform one action, assert one behaviour. One reason to fail per
  test.
- Test observable behaviour, not implementation. A refactor that preserves behaviour should not
  break tests. Do not assert on internal state, private methods, or component instance internals.
- Name tests as behaviour: `it("returns 400 when email is missing")`, not `it("works")`.
- Pyramid: many fast unit tests, fewer integration tests, few E2E. Push logic down into pure
  functions that need no framework to test.
