# API surface protection — Schema contracts

Section of `knowledge/security/api-surface.md`.


Every request body, query parameter set, and header our handlers consume is validated against an
explicit schema at the boundary, before any handler logic runs. Validation is declarative
(pydantic, zod, Bean Validation, FluentValidation) and rejects — it does not coerce and
continue. Unknown fields are rejected, not silently dropped: a client sending a field we do not
model is either confused or probing, and both deserve a 400 that says which field.

Responses serialize from explicit DTOs, never from persistence entities. The DTO is a property
allowlist in both directions: outbound it prevents excessive data exposure (an entity gains a
column, the API does not silently gain a field), inbound it makes privileged fields structurally
unbindable (mass assignment — see the input-validation section of
`knowledge/security/secure-coding-review.md`).

```typescript
// fail: entity in, entity out — every column is now API surface
app.put("/profile", (req) => repo.save(req.body as User));
// pass: explicit contracts both ways, unknown fields rejected
const ProfileUpdate = z.object({ displayName: z.string().max(80), bio: z.string().max(2000) }).strict();
app.put("/profile", (req) => toProfileDto(repo.update(user.id, ProfileUpdate.parse(req.body))));
```

The same rule applies when our code is the client (API10): responses from upstream services and
third-party APIs are external input — schema-validate before use, bound sizes, and never pass
upstream values into our own sinks unencoded.

Verification tests: a request with one unknown field returns 400 naming the field; a request
setting a privileged property (role, price, owner, verified) leaves it unchanged in the store; a
response snapshot for each contract contains exactly the contract's fields and nothing more.
