# TypeScript testing: Vitest, RTL, MSW, Playwright — React Testing Library

Section of `knowledge/typescript/testing.md`.


Render, query as a user would, interact with `userEvent`, assert on the result.

### Query priority (highest first)

1. Accessible to everyone: `getByRole` (with `name`), `getByLabelText`, `getByPlaceholderText`,
   `getByText`.
2. Semantic: `getByAltText`, `getByTitle`.
3. Test id: `getByTestId` — last resort only, when no accessible query fits.

Reaching for `getByTestId` first is a smell: it usually means the markup lacks the accessible role or
label a real user (and a screen reader) relies on. `getByRole` doubles as an accessibility check.

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

test("submits the entered email", async () => {
  const onSubmit = vi.fn();
  render(<SignupForm onSubmit={onSubmit} />);

  await userEvent.type(screen.getByLabelText(/email/i), "a@b.com");
  await userEvent.click(screen.getByRole("button", { name: /sign up/i }));

  expect(onSubmit).toHaveBeenCalledWith({ email: "a@b.com" });
});
```

- Prefer `userEvent` over `fireEvent`: it dispatches the full event sequence a real interaction
  produces (focus, keydown, input, change).
- For async UI, use `findBy*` (retries until present) or `waitFor`; never a fixed `setTimeout`.
- `getBy*` throws when absent (assert presence), `queryBy*` returns null (assert absence),
  `findBy*` is async (assert appearance).

### Custom render helper

Wrap the providers a component needs (router, query client, theme) in one `renderWithProviders` so
each test renders the component in a realistic tree without repeating setup.

```tsx
// src/test/render.tsx
import { render, type RenderOptions } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

export function renderWithProviders(ui: React.ReactElement, options?: RenderOptions) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>, options);
}
```

Disable query retries in tests so a mocked error surfaces on the first attempt instead of retrying.
