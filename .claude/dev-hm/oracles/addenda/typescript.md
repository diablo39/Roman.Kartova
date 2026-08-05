# Oracle addendum: TypeScript / JavaScript / React

Stack-specific checks that extend the core `oracles/security-oracle.md` (SEC-*) and
`oracles/quality-oracle.md` (QUA-*). Only language-specific, deterministic rules live here;
cross-language checks (generic injection, secrets management, auth/z) stay in the core files.
Where a core entry and an addendum entry cover the same defect, the stricter severity and verdict
govern.

## Security (SEC-TS)

| ID | Check | Pass criterion | Sev | Remediation |
|---|---|---|---|---|
| SEC-TS-001 | XSS via `dangerouslySetInnerHTML` | Every `dangerouslySetInnerHTML` value is a string literal/constant or the return of a sanitizer call (e.g. `DOMPurify.sanitize(...)`); zero instances fed variable/props/request data unsanitized. | S0 | `knowledge/typescript/security/xss.md` |
| SEC-TS-002 | Dynamic code execution with non-constant input | Zero `eval`, `new Function`, string-argument `setTimeout`/`setInterval`, `element.innerHTML =`, or `document.write` receiving non-constant data. | S0 | `knowledge/typescript/security/xss.md` |
| SEC-TS-003 | Auth tokens in web storage | Zero `localStorage`/`sessionStorage` writes whose key or value denotes an auth token, JWT, refresh token, session id, or API key. | S1 | `knowledge/typescript/security/token-storage.md` |
| SEC-TS-004 | Secrets in client-reachable code / env exposure | No server-only secret env var read from client-reachable modules; only public-prefixed vars (`VITE_`, `NEXT_PUBLIC_`) used client-side; zero hardcoded credential/key/connection-string literals in the diff. | S0 | `knowledge/typescript/security/secrets.md` |
| SEC-TS-005 | Boundary input validated by a schema | Every HTTP handler, route loader/action, and message consumer parses its external input through a schema validator (e.g. Zod) before use; no direct consumption of `req.body`/`await request.json()`/params typed only via assertion. | S1 | `knowledge/typescript/security/validation.md` |
| SEC-TS-006 | Unsafe type assertion on external data | Zero `as T`, `as unknown as T`, or non-null `!` applied to values originating from network, storage, `JSON.parse`, or `any` without a preceding runtime guard or schema parse. | S1 | `knowledge/typescript/security/validation.md` |
| SEC-TS-007 | CSRF defence on cookie-authenticated mutations | Session cookies set `SameSite=Lax` or `Strict`; every non-idempotent route (`POST`/`PUT`/`PATCH`/`DELETE`) under cookie auth verifies an anti-CSRF token. | S1 | `knowledge/typescript/security/csrf.md` |
| SEC-TS-008 | Dynamic navigation target scheme | Values from variables/props/request data assigned to `href`, `src`, `formAction`/`action`, or passed to `window.open`/`location.assign`/`location.href` pass a scheme allowlist (`https:`, `mailto:`, relative) before use; zero such assignments of non-constant data without a scheme check. | S1 | `knowledge/typescript/security/messaging.md` |
| SEC-TS-009 | Client-side message-passing origin | Every `postMessage` call passes a specific target origin (not `"*"`) for non-public data, and every `message` event handler checks `event.origin` against an allowlist before reading `event.data`. | S1 | `knowledge/typescript/security/messaging.md` |
| SEC-TS-010 | Timing-safe secret comparison and token RNG | Secret/token/signature equality checks use `crypto.timingSafeEqual`; zero `===`/`==`/`.localeCompare` comparisons of a secret against external input, and no `Math.random` used to mint tokens, salts, or ids (matches core SEC-033/SEC-034). | S1 | `knowledge/typescript/security/credentials.md` |
| SEC-TS-011 | Prototype pollution guarded | Recursive merge/extend/set-by-path operations on external input reject the keys `__proto__`, `constructor`, and `prototype`, or operate on null-prototype objects or `Map`s; zero hand-rolled deep merges of external input without such a guard. | S1 | `knowledge/typescript/security/prototype-pollution.md` |

## Quality (QUA-TS)

| ID | Check | Pass criterion | Sev | Remediation |
|---|---|---|---|---|
| QUA-TS-001 | No `any` in changed code | Zero explicit `any` and zero implicit-`any` errors in changed files under strict mode; any retained `any` carries a justified suppression. | S1 | `knowledge/typescript/review-checklist/type-safety.md` |
| QUA-TS-002 | Strict tsconfig maintained | `strict`, `noUncheckedIndexedAccess`, and `exactOptionalPropertyTypes` are enabled and not weakened by the change. | S1 | `knowledge/typescript/platform.md#tsconfig-baseline` |
| QUA-TS-003 | Rules of Hooks | No hook other than `use()` is called conditionally, in a loop, after an early return, or outside a component/hook; `react-hooks/rules-of-hooks` reports clean. | S0 | `knowledge/typescript/review-checklist/react-hooks-and-effects.md` |
| QUA-TS-004 | Complete effect dependencies | `react-hooks/exhaustive-deps` reports clean on changed files; no referenced value is missing from a deps array and no rule-disable comment substitutes for a fix. | S1 | `knowledge/typescript/review-checklist/react-hooks-and-effects.md` |
| QUA-TS-005 | Effect cleanup for external resources | Every effect that opens a subscription, timer, event listener, or connection returns a cleanup that releases it. | S1 | `knowledge/typescript/review-checklist/react-hooks-and-effects.md` |
| QUA-TS-006 | No floating promises | `@typescript-eslint/no-floating-promises` reports clean; every promise is awaited, `.catch`-handled, or explicitly `void`-ed with its own handler. | S1 | `knowledge/typescript/review-checklist/error-handling.md` |
| QUA-TS-007 | Error boundary over fallible subtrees | Each top-level route element and each promise/`use()`-suspended subtree has an ancestor error boundary (or route `errorElement`). | S1 | `knowledge/typescript/review-checklist/error-handling.md` |
| QUA-TS-008 | Catch bindings narrowed | No `catch (e: any)`; catch bindings are `unknown` and narrowed (`instanceof Error`) before member access. | S2 | `knowledge/typescript/review-checklist/error-handling.md` |
| QUA-TS-009 | No unjustified suppression | Zero `@ts-ignore` (use `@ts-expect-error` with a reason); every `eslint-disable` carries a reason comment. | S2 | `knowledge/typescript/review-checklist/type-safety.md` |
| QUA-TS-010 | Stable list keys | Every rendered list assigns `key` from a stable domain identifier; no `key` is the array index, and no `key` is a value generated during render (`Math.random()`, `crypto.randomUUID()` in the map callback). | S2 | `knowledge/typescript/review-checklist/re-renders-and-memoization.md` |
| QUA-TS-011 | Type-only imports under verbatim syntax | With `verbatimModuleSyntax`/`isolatedModules` enabled, type-only imports and exports use `import type`/`export type`; no value is imported solely for use as a type, and no `import type` binding is referenced as a value. | S2 | `knowledge/typescript/review-checklist/module-and-api-hygiene.md` |
