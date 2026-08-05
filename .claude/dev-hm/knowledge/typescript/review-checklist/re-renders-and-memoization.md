# Re-renders and memoization

Confirm whether the React Compiler is enabled for this project (`platform.md`) before flagging
memoization — under the compiler, hand-memoization is usually redundant and adding it is noise.

| Sev | Finding | What to look for |
|---|---|---|
| S0 | Infinite render loop | State set unconditionally during render, or an object/array/function dependency recreated every render feeding an effect that sets state. |
| S2 | New reference passed to a memoized child (no compiler) | Inline object/array/arrow recreated each render defeating `memo`/effect deps. Hoist a constant, or memoize the value/callback. |
| S2 | Expensive compute unmemoized in a hot path (no compiler) | Sorting/filtering large data on every render without `useMemo`. |
| S3 | Speculative memoization | `useMemo`/`useCallback`/`memo` added with no measured re-render problem, or added under the compiler where it is redundant. |
| S2 | Missing stable `key` in a list | Index-as-key on a reorderable/editable list, causing state to attach to the wrong row. |

```tsx
// S2 new reference every render (compiler off) — Child re-renders needlessly
function Parent() {
  const config = { theme: "dark" };      // new object each render
  return <MemoChild config={config} />;
}
// Fix: hoist a stable reference (or useMemo if it depends on props)
const config = { theme: "dark" };
function Parent() { return <MemoChild config={config} />; }
```

```tsx
// S0 infinite render — state set unconditionally during render re-triggers the render
function Counter({ start }: { start: number }) {
  const [n, setN] = useState(start);
  setN(start);                           // schedules a render that runs setN again — loop
  return <span>{n}</span>;
}
// Fix: derive during render, or reset in an effect keyed on the changing input
function Counter({ start }: { start: number }) {
  const [n, setN] = useState(start);
  useEffect(() => setN(start), [start]);
  return <span>{n}</span>;
}
```
