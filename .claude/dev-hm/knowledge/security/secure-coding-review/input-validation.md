# Secure code review by vulnerability class — Input validation

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-040, SEC-042, SEC-044. Every new external input gets validated for type, length,
range, or schema before first use, and validation failure rejects rather than continues with a
default. Prefer declarative schemas (pydantic, zod, Bean Validation, FluentValidation) at the
boundary over ad-hoc `if` chains inside business logic. For SEC-042, redirect targets derived
from input must match an allowlist or be relative paths. Mass assignment (SEC-044):

```csharp
// fail SEC-044: binds every property the client sends, including IsAdmin
app.MapPut("/profile", (User u) => repo.Update(u));
// pass: DTO limited to client-settable fields
record ProfileUpdate(string DisplayName, string Bio);
```

Flag any bind-all onto entities that carry privileged fields (role, price, owner, verified,
balance) — the allowlist must be explicit in code, not assumed from the model.
