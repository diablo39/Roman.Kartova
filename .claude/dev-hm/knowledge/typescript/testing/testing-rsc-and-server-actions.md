# TypeScript testing: Vitest, RTL, MSW, Playwright — Testing RSC and server actions

Section of `knowledge/typescript/testing.md`.


jsdom/RTL cannot render async server components, so test the layers where they live:

- Server actions are plain async functions — import and call them in a node-environment test with
  the session boundary mocked, and pin the security triad from `rsc-and-server-actions.md`:
  rejects unauthenticated, rejects invalid input, performs the authorized happy path. These are
  control-verification tests; they make dropping an auth or schema check a red suite, not a silent
  regression.

```ts
vi.mock("@/lib/auth", () => ({ getSession: vi.fn() }));
import { getSession } from "@/lib/auth";
import { renameProject } from "./actions";

test("rejects an unauthenticated caller", async () => {
  vi.mocked(getSession).mockResolvedValue(null);
  await expect(renameProject({ projectId, name: "x" }))
    .resolves.toEqual({ ok: false, error: "unauthenticated" });
});

test("rejects invalid input before acting", async () => {
  vi.mocked(getSession).mockResolvedValue(session);
  await expect(renameProject({ projectId: "not-a-uuid", name: "" }))
    .resolves.toMatchObject({ ok: false });
});
```

- Server components: extract data-shaping and branching into plain functions and unit-test those.
  Awaiting a simple async component and asserting on the returned element tree works but
  rubber-stamps structure quickly — the snapshot caveat applies.
- Client islands (`"use client"`) are ordinary components: RTL or browser mode as above.
- The integrated seam — form submit → action → revalidate → re-render — is exercised with
  Playwright against the running app; reserve it for critical journeys as with any E2E.
