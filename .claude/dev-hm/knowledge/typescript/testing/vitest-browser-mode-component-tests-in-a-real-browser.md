# TypeScript testing: Vitest, RTL, MSW, Playwright — Vitest browser mode (component tests in a real browser)

Section of `knowledge/typescript/testing.md`.


jsdom is the default fast tier for component tests, but it fakes layout, CSS, and parts of the
event system. Browser mode — stable on the current Vitest major (`knowledge/shared/versions.md`) —
runs the same Vitest suites in a real browser driven by Playwright: the middle tier between jsdom
and full E2E. Use it when the behaviour under test depends on real rendering: CSS-driven
visibility, focus management, pointer gestures, scrolling/virtualization, `ResizeObserver`.

```ts
// vitest.config.ts — browser suites as a separate project; jsdom stays the fast tier
import { defineConfig } from "vitest/config";
import { playwright } from "@vitest/browser-playwright";

export default defineConfig({
  test: {
    projects: [
      { test: { name: "unit", environment: "jsdom", include: ["src/**/*.test.tsx"] } },
      {
        test: {
          name: "browser",
          include: ["src/**/*.browser.test.tsx"],
          browser: { enabled: true, provider: playwright(), instances: [{ browser: "chromium" }] },
        },
      },
    ],
  },
});
```

```tsx
import { render } from "vitest-browser-react";
import { userEvent } from "vitest/browser";

test("menu closes on outside click", async () => {
  const screen = render(<Menu items={items} />);
  await screen.getByRole("button", { name: "Open" }).click();
  await userEvent.click(document.body);
  await expect.element(screen.getByRole("menu")).not.toBeInTheDocument();
});
```

- Locators follow the same query priority as RTL (`getByRole` first), and `expect.element`
  auto-retries until the assertion holds — most `waitFor` boilerplate disappears.
- Events are real browser input, not synthetic dispatch; what passes here matches what a user does.
- MSW runs via its worker (`setupWorker`) instead of `setupServer`; the handler modules are shared.
- Keep browser suites a deliberate subset — slower than jsdom, far cheaper than E2E. Do not move
  the whole unit tier into a browser.
