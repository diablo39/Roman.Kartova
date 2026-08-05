# Error handling

| Sev | Finding | What to look for |
|---|---|---|
| S1 | Unhandled promise rejection | A floating promise (async call not awaited/caught), or `.then()` with no `.catch`. Await it, catch it, or explicitly `void` a fire-and-forget with its own handler. Matches oracle QUA-TS-006. |
| S1 | Missing React error boundary | A subtree that fetches or renders untrusted content with no `<ErrorBoundary>` (or route `errorElement`) above it; one throw blanks the app. |
| S1 | Swallowed error | `catch {}` empty, or `catch` that logs and continues as if success. Rethrow, surface, or handle deliberately. |
| S2 | Async error not handled in a handler | Express/route handler `async` body whose throw is not forwarded to error middleware / caught. |
| S2 | Rejection typed as anything but unknown | `catch (e: any)` then `e.message`. Catch is `unknown`; narrow with `instanceof Error`. |

```ts
// S1 floating promise — rejection is unobservable
saveUser(user);
// Fix: await (or void with an explicit catch for fire-and-forget)
await saveUser(user);
void saveUser(user).catch(reportError);
```

```tsx
// S1 no boundary — one throw in a fetching/suspended subtree blanks the whole app
<Suspense fallback={<Spinner />}><Profile /></Suspense>
// Fix: wrap fallible subtrees in an error boundary (or a route errorElement)
<ErrorBoundary fallback={<Failed />}>
  <Suspense fallback={<Spinner />}><Profile /></Suspense>
</ErrorBoundary>
```
