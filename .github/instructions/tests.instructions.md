---
applyTo: "**/*Tests.cs,**/*Tests/**/*.cs,web/src/**/*.{test,spec}.{ts,tsx}"
---

## Don't comment on
- Test method naming when the file is internally consistent
- Mocking-library swaps (one lib per assembly)
- "Extract a helper" when existing fixtures cover the case
- Mutation score, coverage %, `[ExcludeFromCodeCoverage]` placement — other gates own these
- The `Tests` vs `IntegrationTests` project split — intentional
- Missing `await` on `*Async()` calls — CS4014 covers it

## Backend tests (xUnit)
- Use `[Fact]`, `[Theory]`, `[InlineData]`, `[MemberData]`. Flag NUnit `[Test]`/`[TestCase]` and MSTest `[TestMethod]`/`[TestClass]`.
- NetArchTest assertions live in the architecture-tests project. Flag `Types.InAssembly(...)` / `TypesThat()...Should()` in unit or integration test files.
- Integration tests run on real Postgres via Testcontainers. Flag `UseInMemoryDatabase`, `UseSqlite("Data Source=:memory:")`, `Microsoft.EntityFrameworkCore.InMemory`.
- Tenant-scoped tests derive from `KartovaApiFixtureBase` (or `KeycloakContainerFixture`). Flag `new XyzDbContext(...)` inline or `GetRequiredService<XyzDbContext>()` outside fixture scope.
- No `.Result` / `.Wait()` in async test bodies.
- List-endpoint integration tests assert the `CursorPage<T>` envelope. Flag bare `Should().HaveCount(...)` or array-shape on paginated endpoints.
- Error-path integration tests assert `problem+json` (`type`, `title`, `status`, `traceId`). Flag status-code-only or string-body matching on errors.

## Frontend tests (Vitest + Testing Library)
- Vitest with `@testing-library/react` and `@testing-library/user-event`. Flag `jest.mock` / `jest.fn` / `jest.config.*`, `react-dom/test-utils`, `enzyme`.
- Query by role or accessible name. Flag `container.querySelector(".css-class")`, `getByTestId` when `getByRole`/`getByLabelText` exists, assertions on hook return values.
- Interactions through `userEvent` (`await user.click`, `await user.type`). Flag `fireEvent.click`/`fireEvent.change` for user actions.
- Mock HTTP via `vi.mock` of the typed `openapi-fetch` client. Flag `vi.spyOn(globalThis, "fetch")`, `vi.fn()` patches against `window.fetch`, hand-built `new Response(...)`, MSW handlers (not in stack).
- Async UI: `await screen.findBy*` or `await waitFor(...)`. Flag synchronous `getBy*` against async state.

## Assertions
- Flag tests whose only assertion is `Should().NotBeNull()`, `Should().NotBeEmpty()`, `expect(x).toBeDefined()`, `expect(x).toBeTruthy()`, or `not.toThrow()`.
- Flag value comparisons checking only `.GetType()`, `typeof`, `instanceof`, or property existence without reading the value.

## Quick reference
| Don't                                         | Do                                              |
|-----------------------------------------------|-------------------------------------------------|
| `[Test]` / `[TestMethod]`                     | `[Fact]` / `[Theory]`                           |
| `UseInMemoryDatabase` in integration test     | Testcontainers Postgres via fixture             |
| `new XyzDbContext(opts)` in test body         | resolve via `KartovaApiFixtureBase` scope       |
| `Types.InAssembly(...)` in `*.Tests` project  | move to architecture-tests project              |
| `Should().NotBeNull()` alone                  | assert specific field values                    |
| `Should().HaveCount(...)` on paginated        | assert `CursorPage<T>` envelope                 |
| `StatusCode.Should().Be(400)` only            | assert `problem+json` shape too                 |
| `fireEvent.click(btn)`                        | `await user.click(btn)`                         |
| `vi.spyOn(globalThis, "fetch")`               | `vi.mock` of typed client                       |
