# React data and state: TanStack Query, client stores, forms

How client-side React apps manage server data, UI state, and forms. The one-paragraph summary in
`platform.md` (data layer) points here; server-side fetching in RSC apps is in
`rsc-and-server-actions.md`. Pinned library versions are in `knowledge/shared/versions.md`.

## Classify the state first

| Kind | Owner | Not this |
|---|---|---|
| Server state (fetched, cacheable, shared with the backend) | TanStack Query | a client store, `useEffect` + `useState` |
| URL state (filters, tab, pagination — shareable/bookmarkable) | the router's search params | component state that dies on reload |
| Form state (drafts, per-field errors) | react-hook-form | a global store |
| Client UI state (modals, selection, theme) | `useState` → lift → context → small store | TanStack Query |

Never mirror server data into a client store "for convenience": two owners of one fact guarantees
staleness bugs. The cache is the single source of server truth; components read it through queries.

## TanStack Query

### Query keys and `queryOptions`

Keys are the cache identity and the invalidation surface. Make them hierarchical arrays and define
them once, next to the fetcher, with the `queryOptions` helper — every consumer (`useQuery`,
`useSuspenseQuery`, prefetch, invalidate) then shares one typed definition:

```ts
import { queryOptions } from "@tanstack/react-query";

export const userQueries = {
  all: () => ["users"] as const,
  detail: (id: string) =>
    queryOptions({
      queryKey: ["users", "detail", id] as const,
      queryFn: ({ signal }) => fetchUser(id, { signal }), // signal: unmount/refetch cancels
      staleTime: 30_000,
    }),
};
```

Passing the provided `signal` into `fetch` gives cancellation on unmount and on key change for free
(the review checklist's missing-cancellation finding). Validate the response with a schema inside
the fetcher (`security.md#validation`) so the cache only ever holds trusted shapes.

### Freshness: `staleTime` vs `gcTime`

- `staleTime`: how long data is fresh — no automatic refetch on mount/focus/reconnect while fresh.
  Default is 0 (always stale). Set it per data class (seconds for dashboards, minutes for
  reference data) instead of globally disabling `refetchOnWindowFocus`.
- `gcTime`: how long unused cache entries survive before eviction; only relevant for
  back-navigation warmth.
- Status fields: `isPending` (no data yet — first load), `isFetching` (any fetch, including
  background refresh). Render skeletons on `isPending`, a subtle indicator on `isFetching`.

### Suspense integration

`useSuspenseQuery` returns non-nullable `data` and delegates pending/error to the nearest
`<Suspense>`/error boundary (QUA-TS-007). Start sibling queries in parallel with
`useSuspenseQueries`, or prefetch above the boundary, to avoid request waterfalls.

### Mutations, invalidation, optimistic updates

```ts
const qc = useQueryClient();
const createUser = useMutation({
  mutationFn: postUser,
  onSettled: () => qc.invalidateQueries({ queryKey: userQueries.all() }),
});
```

- Invalidate by key prefix (`["users"]` hits every user query) in `onSettled`, so both success and
  a rolled-back optimistic update re-sync with the server.
- Optimistic UI, simplest first: render `createUser.variables` while `createUser.isPending` — no
  cache surgery, the pending row disappears or becomes real on settle.
- Cache-write optimism only when many components must see the change immediately:

```ts
onMutate: async (next) => {
  await qc.cancelQueries({ queryKey: key });          // don't let a refetch clobber the write
  const prev = qc.getQueryData(key);
  qc.setQueryData(key, next);
  return { prev };                                     // context for rollback
},
onError: (_err, _next, ctx) => qc.setQueryData(key, ctx?.prev),
onSettled: () => qc.invalidateQueries({ queryKey: key }),
```

### Pagination and infinite queries

- Paged tables: `placeholderData: keepPreviousData` keeps the previous page rendered while the next
  loads — no layout flash, `isPlaceholderData` marks the transition.
- Feeds: `useInfiniteQuery` with `initialPageParam` and `getNextPageParam`; set `maxPages` so an
  hour of scrolling does not pin every page in memory. Pair with virtualization
  (`performance.md`) for long lists.

### Errors and retries

- Queries retry a few times with backoff by default — disable retries in tests (`testing.md`) and
  for non-idempotent or fast-failing endpoints.
- `throwOnError: true` routes irrecoverable errors to the error boundary; recoverable ones render
  inline from `error`. A global `QueryCache` `onError` centralizes toast-level reporting.

## Client state: the smallest tool that fits

- Start with `useState`/`useReducer` in the component; lift to the closest common parent when two
  components share it.
- Context is for low-frequency values (theme, session, locale): every consumer re-renders on
  change, so it is not a store for hot state.
- For cross-cutting client UI state in new work, a Zustand store is the default: no provider,
  selector-based subscriptions re-render only what changed, and stores are plain modules that test
  without React. Keep stores small and domain-scoped; define actions inside the store.
- Jotai (atomic model) fits fine-grained derived-state graphs; Redux Toolkit stays where a Redux
  store already exists — migrate deliberately or not at all, never run two store libraries side by
  side in one app.

```ts
import { create } from "zustand";

type PanelState = {
  openPanelId: string | null;
  open: (id: string) => void;
  close: () => void;
};
export const usePanel = create<PanelState>()((set) => ({
  openPanelId: null,
  open: (id) => set({ openPanelId: id }),
  close: () => set({ openPanelId: null }),
}));

// component: subscribes to one slice only
const openPanelId = usePanel((s) => s.openPanelId);
```

Keep server data (TanStack Query), form state (react-hook-form), and URL state (router) out of
these stores — the store holds only what no other owner covers.

## Forms: react-hook-form + Zod

react-hook-form registers uncontrolled inputs, so typing does not re-render the form tree; use
`useWatch` to observe single fields where live derived UI is needed. Validation delegates to the
schema through a resolver — `@hookform/resolvers` bridges any Standard Schema validator, which the
current Zod major implements.

```tsx
import { useForm } from "react-hook-form";
import { standardSchemaResolver } from "@hookform/resolvers/standard-schema";
import { z } from "zod";

const SignupSchema = z.object({
  email: z.email(),
  age: z.coerce.number().int().min(18),
});
type SignupValues = z.infer<typeof SignupSchema>;

function SignupForm() {
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } =
    useForm<SignupValues>({ resolver: standardSchemaResolver(SignupSchema) });

  const onSubmit = handleSubmit(async (values) => {
    const result = await api.signup(values);            // typed client, api-contracts.md
    if (!result.ok) setError("root.server", { message: result.problem.title });
  });

  return (
    <form onSubmit={onSubmit}>
      <label htmlFor="email">Email</label>
      <input id="email" type="email" {...register("email")} aria-invalid={!!errors.email} />
      {errors.email && <p role="alert">{errors.email.message}</p>}
      <button disabled={isSubmitting}>Sign up</button>
      {errors.root?.server && <p role="alert">{errors.root.server.message}</p>}
    </form>
  );
}
```

- One schema, two uses: the same Zod module validates on the client (UX) and on the server
  (the control — SEC-TS-005). Client-side validation is never the security boundary; the server
  re-parses every submission.
- Map server-side field errors back with `setError("fieldName", …)`; non-field failures go under
  `root.*` so they clear on the next submit.
- Label every field and expose errors with `role="alert"`/`aria-describedby` — the RTL queries in
  `testing.md` then double as the accessibility check.
- Field arrays (`useFieldArray`) cover dynamic rows; give rows stable ids from the hook, not array
  indexes (QUA-TS-010).
- In RSC apps, simple forms can post straight to a server action (`rsc-and-server-actions.md`);
  react-hook-form remains the client-side layer for multi-field validation UX on complex forms.
