# React hooks and effects

| Sev | Finding | What to look for |
|---|---|---|
| S0 | Rules of Hooks violation | Hook called conditionally, in a loop, after an early `return`, or outside a component/hook. (`use()` may be conditional; other hooks may not.) |
| S1 | Incomplete effect dependencies | `useEffect`/`useMemo`/`useCallback` deps array missing a referenced value. Fix the deps, do not silence `react-hooks/exhaustive-deps`. |
| S1 | Effect used to derive state | State computed in an effect from props/state that could be derived during render. Compute in render; drop the effect. |
| S1 | Missing effect cleanup | Subscription, timer, listener, or connection opened in an effect with no cleanup return — a leak (see `debugging.md`). |
| S1 | Stale closure in a long-lived callback | `setInterval`/subscription reading state directly instead of the functional updater `setX(prev => …)`. |

```tsx
// S1 stale closure: always adds to the initial count
useEffect(() => {
  const id = setInterval(() => setCount(count + 1), 1000);
  return () => clearInterval(id);
}, []);
// Fix: functional update, no stale capture
useEffect(() => {
  const id = setInterval(() => setCount(prev => prev + 1), 1000);
  return () => clearInterval(id);
}, []);
```
