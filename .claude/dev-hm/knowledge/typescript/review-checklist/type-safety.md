# Type safety

| Sev | Finding | What to look for |
|---|---|---|
| S1 | `any` in new/changed code | Explicit `any`, implicit `any` from an un-annotated param, or `any` leaking from an untyped dependency. Replace with `unknown` + narrowing or a precise type. |
| S1 | Assertion instead of validation on external data | `as T`, `as unknown as T`, or `!` on values from network/storage/`JSON.parse`. Validate with a schema (see `security.md`). |
| S2 | Suppression without justification | `@ts-ignore` (use `@ts-expect-error` with a reason comment), or a disabled lint rule with no explanation. |
| S2 | Boolean/optional soup for exclusive states | `{ isLoading; data?; error? }` where states are mutually exclusive. Use a discriminated union (`platform.md`). |
| S2 | Non-null `!` used as a shortcut | `!` hiding a real nullable path instead of a guard or default. |
| S3 | Widening annotation over `satisfies` | Config typed with a widening annotation that loses literal types. |

Non-negotiable base: strict `tsconfig` (`strict`, `noUncheckedIndexedAccess`,
`exactOptionalPropertyTypes`). A change that weakens these settings is S1.

```tsx
// S1 assertion instead of validation — a malformed response is trusted as User at runtime
const user = (await res.json()) as User;
// Fix: parse through the schema; `user` is validated and typed (security.md#validation)
const user = UserSchema.parse(await res.json());
```
