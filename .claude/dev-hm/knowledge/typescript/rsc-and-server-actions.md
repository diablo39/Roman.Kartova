# React Server Components and server actions

Boundary rules, defensive patterns for server actions, and the caching/streaming model. The
semantics here are React's; the mainstream host is the current Next.js App Router generation
(`knowledge/shared/versions.md`), and the security rules extend SEC-TS-004/005 in
`oracles/addenda/typescript.md` to the server boundary. Client-side data belongs to
`react-data-and-state.md`; secret-exposure rules to `security.md#secrets`.

Adopting (or leaving) an RSC full-stack architecture versus a SPA plus API is a one-way door —
analyze it per `knowledge/shared/three-framing-analysis.md` before committing.

## The boundary model

Components are server components by default: they render on the server (or at build), may be
`async`, may read databases and secrets, and ship no JS for their own logic. `"use client"` marks
the entry into the client graph — that module and everything it imports becomes client code.

| | Server component | Client component |
|---|---|---|
| Read DB / secrets / server env | yes | never |
| `async`/`await` in the component | yes | no — read promises with `use()` |
| State, effects, browser APIs, event handlers | no | yes |
| Can import | server and client modules | client modules only |
| Can render | both (client children get serialized props) | client components; server components only via `children`/props passed from a server parent |

- Props crossing server → client must be serializable by React's RSC protocol (plain data, Dates,
  Maps/Sets; no functions except server actions, no class instances).
- A client component cannot import a server component, but a server parent can pass one as
  `children` — composition is how interactive shells wrap server-rendered content.
- Keep `"use client"` at the leaves. Marking a layout or page client drags its whole import graph
  into the bundle.

## Enforcing the boundary (defensive)

- Add `import "server-only"` to every module that touches secrets, tokens, or the database. The
  build then fails if any client graph pulls it in — turning an SEC-TS-004 leak into a compile
  error. Use `client-only` for the inverse.
- Only public-prefixed env vars may reach client code (`security.md#secrets`); everything else is
  server-only by construction.
- Serialized props are visible to the client — in the HTML/RSC payload, rendered or not. Passing a
  whole user or config object across the boundary ships every field. Select explicitly:
  `<Profile name={user.name} avatarUrl={user.avatarUrl} />`, never `<Profile user={user} />` when
  `user` carries roles, emails, or tokens.
- React's taint APIs (`experimental_taintObjectReference`, `experimental_taintUniqueValue`) make
  accidental passing of a tainted object/value across the boundary throw. They are experimental —
  use them as defence in depth where enabled, never as the only control.

## Server actions

`"use server"` exposes a function as an endpoint callable from the client — via a form `action`,
`startTransition`, or a direct call. Compile-time reality: every exported server action is a
public, unauthenticated HTTP endpoint that an attacker can invoke directly with arbitrary
arguments, regardless of which page renders it, whether any page renders it, or what checks the
rendering page performed. Write every action like an exposed route handler:

```ts
"use server";
import { z } from "zod";
import { getSession } from "@/lib/auth";       // server-only module

const RenameProject = z.object({
  projectId: z.uuid(),
  name: z.string().min(1).max(120),
});

export async function renameProject(raw: unknown) {
  const session = await getSession();                    // 1. authenticate
  if (!session) return { ok: false as const, error: "unauthenticated" };

  const parsed = RenameProject.safeParse(raw);           // 2. validate (SEC-TS-005)
  if (!parsed.success) return { ok: false as const, error: "invalid input" };

  const allowed = await canEditProject(session.userId, parsed.data.projectId);
  if (!allowed) return { ok: false as const, error: "forbidden" };  // 3. authorize the object

  const project = await db.renameProject(parsed.data);   // 4. act
  revalidateTag(`project:${project.id}`);
  return { ok: true as const, name: project.name };      // 5. return minimal data
}
```

Rules, in order of how often they are violated:

- Authenticate and authorize inside the action body. A middleware/proxy-layer check is perimeter
  hardening, not the control — routing-layer auth has been bypassed in practice (the Next.js
  middleware authorization bypass, CVE-2025-29927). The action re-checks, every time, including
  object-level authorization against the ids in the input (core SEC-010–012).
- Validate every argument with a schema, `FormData` included (`Object.fromEntries(formData)` then
  parse). Action parameters are attacker-controlled input; the TypeScript signature guarantees
  nothing at runtime. This extends SEC-TS-005 to actions.
- Closed-over values in inline actions travel through the client. The framework encrypts them, but
  encryption is transport secrecy, not authorization — never trust a closed-over `isAdmin` or
  price; re-derive privileges from the session inside the action.
- Dead actions are live endpoints. An exported action that no page uses anymore still accepts
  requests — delete it. Prefer dedicated `"use server"` files over inline closures so the exposed
  surface is enumerable.
- Return minimal data. The return value serializes to the caller: a full DB row leaks hashes,
  roles, and internal flags. Return a purpose-built result object — expected failures as a
  discriminated result (pairs with `useActionState`), not thrown errors, which frameworks mask in
  production and cost the client its typed handling.
- Rate-limit expensive or auth-adjacent mutations (`security.md#node`). Hosts POST-only actions and
  check `Origin`/`Host`, which covers classic CSRF for same-site cookie setups — treat that as one
  layer, and keep SEC-TS-007 for any cookie-authenticated routes outside the action system.

On the client, `useActionState(action, initial)` gives `[state, formAction, isPending]` for forms;
`useOptimistic` renders the optimistic value while the action runs (`platform.md`).

## Caching and streaming

The current model (the Cache Components generation) is dynamic by default: nothing renders from a
shared cache unless a function or component opts in with `"use cache"`. That default is the
security posture — per-request data never lands in a shared cache by accident.

- Cache only shared, requester-independent data (catalogs, marketing content, reference lists).
  Tag entries (`cacheTag(...)`) and invalidate on mutation (`revalidateTag`/`updateTag` for
  read-your-writes) rather than relying on expiry.
- Never `"use cache"` a function whose result depends on the requester — session, cookies, or an
  id derived from them. A shared cache entry of per-user data is a cross-user leak (SEC-TS-004
  extended to cache scope). Personalized content stays dynamic or uses the framework's explicit
  per-user/private cache variant; when in doubt, leave it dynamic.
- Older codebases from the previous framework generation cached `fetch` results implicitly. When
  working there, verify each fetch's cache mode explicitly instead of assuming dynamic-by-default.

Streaming shape: the static shell renders immediately; slow, dynamic subtrees sit behind
`<Suspense>` boundaries and stream in as they resolve.

- Every suspended subtree needs an error boundary above it (QUA-TS-007) — a route-level
  `error.tsx`/`errorElement` at minimum.
- Avoid server waterfalls: start independent fetches before awaiting any
  (`Promise.all`, or kick off promises early and pass them down for children to `use()`).
- Do not gate the whole page on its slowest query — wrap the slow widget, not the layout.

## Testing

Server actions test as plain async functions (assert the reject-unauthenticated /
reject-invalid-input / authorized-happy-path triad); client islands test with RTL or browser mode;
the full server/client seam is Playwright's job. Details in `testing.md`.
