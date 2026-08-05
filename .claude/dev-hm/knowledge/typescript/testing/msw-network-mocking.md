# TypeScript testing: Vitest, RTL, MSW, Playwright — MSW (network mocking)

Section of `knowledge/typescript/testing.md`.


Mock at the network layer with request handlers, not by stubbing `fetch`/`axios`. The same handlers
serve unit tests (`setupServer`) and the browser (`setupWorker`), so tests exercise the real client
code path.

```ts
// src/test/mocks/handlers.ts
import { http, HttpResponse } from "msw";

export const handlers = [
  http.get("/api/users/:id", ({ params }) =>
    HttpResponse.json({ id: params.id, name: "Ada" })),
  http.post("/api/users", async ({ request }) =>
    HttpResponse.json({ id: 1, ...(await request.json() as object) }, { status: 201 })),
];

// src/test/setup.ts
import { setupServer } from "msw/node";
import { handlers } from "./mocks/handlers";
const server = setupServer(...handlers);
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
```

Override per test to exercise error and empty states — the paths bugs hide in:

```ts
server.use(http.get("/api/users/:id", () => new HttpResponse(null, { status: 500 })));
```

`onUnhandledRequest: "error"` fails the test on any request without a handler, so a forgotten mock
surfaces immediately instead of hitting the network.
