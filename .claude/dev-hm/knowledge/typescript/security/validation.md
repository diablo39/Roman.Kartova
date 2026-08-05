# TypeScript / web security patterns — Boundary validation with a schema {#validation}

Section of `knowledge/typescript/security.md`.


Every value entering the program from outside — HTTP body/query/params, route loader/action input,
message payloads, third-party API responses, `localStorage`, environment variables — is untrusted
and typed `unknown` until a schema validates it. Type assertions (`as User`) are compile-time only;
they do nothing at runtime and are the single most common way malformed or hostile data gets treated
as valid.

```ts
import { z } from "zod";

const CreateUser = z.object({
  email: z.email(),                 // top-level format schema; z.string().email() is deprecated
  age: z.number().int().min(0).max(150),
});

// Express / any Node handler
app.post("/users", async (req, res) => {
  const parsed = CreateUser.safeParse(req.body); // never trust req.body directly
  if (!parsed.success) return res.status(400).json({ errors: parsed.error.issues });
  await createUser(parsed.data); // parsed.data is fully typed and validated
});
```

Rules:
- Validate at the edge, once, then pass the inferred type inward. Do not re-assert deeper in.
- `safeParse` for user input (return 400 on failure); `parse` (throws) only where a throw is handled.
- Validate environment variables at startup through one schema so a missing/invalid var fails fast
  instead of surfacing as `undefined` deep in the code.
- A validated third-party response prevents a compromised or changed upstream from injecting shapes
  your types promised but the network never guaranteed.
