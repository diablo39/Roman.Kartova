---
applyTo: "**/*Tests.cs,**/*Tests/**/*.cs,web/src/**/*.{test,spec}.{ts,tsx}"
---

## Don't comment on
- Test method naming style
- Mocking-library swaps (one lib per assembly)
- "Extract a helper" when existing fixtures cover the case
- Mutation score, coverage %, `[ExcludeFromCodeCoverage]` placement — other gates own these
- The `Tests` vs `IntegrationTests` project split — intentional

## Backend tests (MSTest v4 — ADR-0097)
- Use `[TestClass]` + `[TestMethod]` + `[DataRow]`. Flag xUnit `[Fact]`/`[Theory]`/`[InlineData]`/`[MemberData]` and NUnit `[Test]`/`[TestCase]`.
- Assertions are MSTest native: `Assert.AreEqual`, `Assert.IsNotNull`, `Assert.ThrowsExactly<T>`, `StringAssert.*`, `CollectionAssert.*`. Flag `FluentAssertions` and `Assert.That`.
- NetArchTest assertions live in the architecture-tests project. Flag `Types.InAssembly(...)` / `TypesThat()...Should()` in unit/integration files.
- Integration tests run on real Postgres via Testcontainers. Flag `UseInMemoryDatabase`, `UseSqlite("Data Source=:memory:")`, `Microsoft.EntityFrameworkCore.InMemory`.
- Tenant-scoped tests derive from `KartovaApiFixtureBase`. Flag `new XyzDbContext(...)` inline or `GetRequiredService<XyzDbContext>()` outside fixture scope.
- No `.Result` / `.Wait()` in async test bodies.
- List-endpoint integration tests assert the `CursorPage<T>` envelope. Flag bare count checks on paginated endpoints.
- Error-path integration tests assert `problem+json` (`type`, `title`, `status`, `traceId`). Flag status-code-only or string-body matching on errors.

## Frontend tests (Vitest + Testing Library)
- Vitest with `@testing-library/react` and `@testing-library/user-event`. Flag `jest.mock` / `jest.fn` / `jest.config.*`, `react-dom/test-utils`, `enzyme`.
- Query by role or accessible name. Flag `container.querySelector(".css-class")`, `getByTestId` when `getByRole`/`getByLabelText` exists, assertions on hook return values.
- Interactions through `userEvent` (`await user.click`, `await user.type`). Flag `fireEvent.click`/`fireEvent.change` for user actions.
- Mock HTTP via `vi.mock` of the typed `openapi-fetch` client. Flag `vi.spyOn(globalThis, "fetch")`, `vi.fn()` patches against `window.fetch`, hand-built `new Response(...)`, MSW handlers (not in stack).
- Async UI: `await screen.findBy*` or `await waitFor(...)`. Flag synchronous `getBy*` against async state.

## Assertions
- Flag tests whose only assertion is `Assert.IsNotNull(...)`, `Assert.IsTrue(...)`, `expect(x).toBeDefined()`, or `expect(x).toBeTruthy()`.
- Flag value comparisons checking only `.GetType()`, `typeof`, `instanceof`, or property existence without reading the value.

## Quick reference
| Don't                                         | Do                                              |
|-----------------------------------------------|-------------------------------------------------|
| `[Fact]` / `[Theory]` / `[Test]`              | `[TestMethod]` / `[DataRow]`                    |
| `obj.Should().Be(x)`                          | `Assert.AreEqual(x, obj)`                       |
| `act.Should().Throw<T>()`                     | `Assert.ThrowsExactly<T>(() => act())`          |
| `UseInMemoryDatabase` in integration test     | Testcontainers Postgres via fixture             |
| `new XyzDbContext(opts)` in test body         | resolve via `KartovaApiFixtureBase` scope       |
| `Types.InAssembly(...)` in `*.Tests` project  | move to architecture-tests project              |
| `Assert.IsNotNull(x)` alone                   | assert specific field values                    |
| count-only check on paginated                 | assert `CursorPage<T>` envelope                 |
| status-code-only on errors                    | assert `problem+json` shape                     |
| `fireEvent.click(btn)`                        | `await user.click(btn)`                         |
| `vi.spyOn(globalThis, "fetch")`               | `vi.mock` of typed client                       |
