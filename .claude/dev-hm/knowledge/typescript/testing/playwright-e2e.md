# TypeScript testing: Vitest, RTL, MSW, Playwright — Playwright (E2E)

Section of `knowledge/typescript/testing.md`.


Reserve E2E for critical user journeys (auth, checkout, the one flow that must never break). Keep
them few; they are the slowest and flakiest tier.

### Page Object Model

Encapsulate selectors and actions per page so a UI change touches one file.

```ts
// pages/LoginPage.ts
import type { Page, Locator } from "@playwright/test"; // type-only — import type (QUA-TS-011)

export class LoginPage {
  readonly email: Locator;
  readonly password: Locator;
  readonly submit: Locator;
  constructor(private page: Page) {
    this.email = page.getByLabel("Email");
    this.password = page.getByLabel("Password");
    this.submit = page.getByRole("button", { name: "Sign in" });
  }
  async goto() { await this.page.goto("/login"); }
  async login(email: string, password: string) {
    await this.email.fill(email);
    await this.password.fill(password);
    await this.submit.click();
  }
}
```

- Use role/label/text locators (`getByRole`, `getByLabel`, `getByText`), matching RTL priority; avoid
  brittle CSS/XPath.
- Rely on web-first assertions (`await expect(locator).toBeVisible()`) that auto-wait; never a fixed
  sleep. Use `expect(page).toHaveURL(...)` for navigation.
- Isolate state per test (fresh storage/auth via fixtures), run in parallel, and enable `trace`,
  `screenshot`, and `video` on retry to diagnose CI-only failures.
