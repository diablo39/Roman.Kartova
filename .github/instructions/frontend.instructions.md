---
applyTo: "web/src/**/*.{ts,tsx}"
---

## Don't comment on
- Prettier / ESLint formatting, import order
- Arrow vs function-declaration component style
- `useMemo` / `useCallback` "for perf" without measured evidence
- Component file size — `/simplify` covers it
- Suggesting MUI / Chakra / Mantine / shadcn — Untitled UI is fixed
- Suggesting RTK Query / SWR / GraphQL — TanStack Query is fixed
- Anything under `web/src/generated/` — regenerated; never hand-edit

## Components & UI
- Interactive primitives use `react-aria-components` (Untitled UI). Flag new native `<button>`, `<select>`, `<textarea>`, non-hidden `<input>` in interactive components.
- Icon-only `<Button>` / `<a>` carries `aria-label` (or visually-hidden text). Flag missing accessible name.
- Icons via `@untitledui/icons`. Flag `lucide-react`, `react-icons`, ad-hoc `<svg>` as new icon sources.
- Tailwind v4 utilities for styling. Flag inline `style={{...}}` for layout / spacing / color (dynamic-value escape hatch fine).
- Dark mode toggles `.dark-mode` (NOT `.dark`). Theme switching via `next-themes`. Flag `dark:` Tailwind variants and direct DOM class manipulation.

## Forms & validation
- Forms use `react-hook-form` + `zod` via `@hookform/resolvers/zod`. Flag `useState`-driven field state in multi-field forms.
- Schemas next to component or in sibling `*.schema.ts`. Flag inline ad-hoc validation chains.

## Data fetching & server state
- Server state via TanStack Query: `useQuery` / `useMutation` / `useInfiniteQuery`. Flag `useState` + `useEffect` + `fetch` for server data.
- HTTP via the generated `openapi-fetch` client (`client.GET`, `client.POST`). Flag raw `fetch(...)` / `axios` to project endpoints.
- Query consumers handle `isPending` + `isError`. Flag destructuring `data` without a loading or error branch.
- API errors are RFC 7807 `problem+json` (`type`, `title`, `detail`, `traceId`, `errors`). Flag string-matching on `e.message`.

## Routing & auth
- Routing via `react-router-dom` v7 data router APIs (`createBrowserRouter`, loaders, actions). Flag v5 patterns: `<Switch>`, `history` v5, class-component route guards.
- Auth via `react-oidc-context` `useAuth()`. Flag manual JWT decode, raw token storage in `localStorage` / `sessionStorage`, hand-rolled refresh.

## Lists & pagination
- List screens compose `useCursorList` + `useListUrlState` + `<DataTable>`. Cursor pagination is non-optional.
- Queries use `?sortBy=&sortOrder=asc|desc&cursor=&limit=` (max 200). Flag `?page=`, `?offset=`, `?perPage=`.

## TypeScript
- Strict mode. Prefer `unknown` + narrowing over `any`. Flag new `any`. Replace `@ts-ignore` with `@ts-expect-error` + reason.

## ❌ / ✅ quick reference
| Concern       | ❌                                  | ✅                                  |
|---------------|-------------------------------------|-------------------------------------|
| Server state  | `useState`+`useEffect`+`fetch`      | `useQuery` (TanStack)               |
| HTTP          | `fetch("/api/x")` / `axios`         | `client.GET("/x")`                  |
| Forms         | per-field `useState`                | `useForm` + `zodResolver`           |
| Auth          | manual JWT decode                   | `useAuth()`                         |
| Lists         | `useState([])` + manual paging      | `useCursorList` + `<DataTable>`     |
| Icons         | inline `<svg>` / `lucide-react`     | `@untitledui/icons`                 |
| Icon-only btn | `<Button><Icon/></Button>`          | `<Button aria-label="…">`           |
| Type escape   | `any` / `@ts-ignore`                | `unknown` + narrow / `@ts-expect-error` |
