# Copilot review & coding instructions for Kartova

Path-scoped instructions in `.github/instructions/`:
- `backend.instructions.md` — `src/**/*.cs` (Wolverine, EF Core, tenant scope, modular monolith)
- `frontend.instructions.md` — `web/src/**/*.{ts,tsx}` (React 19, Untitled UI, TanStack Query)
- `tests.instructions.md` — `**/*Tests*.cs` and `web/src/**/*.{test,spec}.{ts,tsx}`

These files serve **both** Copilot code review and Copilot coding agent (no `excludeAgent`). Review is advisory — branch protection + the Definition of Done gate merges.

## Repo-wide rules
- Zero-warning build (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`). Flag changes that introduce warnings even if compilation succeeds.
- Dates in code, comments, commit messages: absolute `YYYY-MM-DD`. Flag relative dates ("Thursday", "next sprint", "recently").
- Solution file is `Kartova.slnx`. Don't suggest reintroducing classic `.sln`.
- Modular monolith — one csproj tree per bounded context. Modules interact only via Wolverine `IMessageBus` or Kafka events; cross-module references go through `*.Contracts` packages only.

## Global deny-list (banned alternatives — don't propose, don't generate)
- REST → GraphQL — banned across the codebase
- Wolverine → MediatR or MassTransit — banned
- PostgreSQL Row-Level Security → schema-per-tenant — banned
- HTTPS polling → gRPC streaming for the agent transport — banned
- Untitled UI (`react-aria-components` + Tailwind v4) → other design systems — banned
- TanStack Query → RTK Query, SWR, GraphQL clients — banned
- `[ExcludeFromCodeCoverage]` on production logic — narrow scope defined in `backend.instructions.md`
