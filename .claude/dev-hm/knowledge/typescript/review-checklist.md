# TypeScript / React review checklist

Severity-tiered catalogue of TS/JS/React review findings. Tiers (S0 Block · S1 Must-fix ·
S2 Should-fix · S3 Advisory) are defined in `knowledge/shared/severity-tiers.md`. Security findings
and the SEC-TS oracle entries live in `security.md`; type-system baseline is in `platform.md`.
Report each finding as `ID or rule — file:line — one-line reason`, with a before/after only when
the fix is non-obvious.

The core oracle's S0 entries still apply to every TS change even though this checklist does not
restate them — hardcoded secrets (SEC-020), missing authentication/authorization (SEC-010 –
SEC-012), injection (SEC-001 – SEC-004), and disabled certificate validation (SEC-031) in
`oracles/security-oracle.md`. A language-only review walk must still check them.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Type safety | `knowledge/typescript/review-checklist/type-safety.md` |
| React hooks and effects | `knowledge/typescript/review-checklist/react-hooks-and-effects.md` |
| Re-renders and memoization | `knowledge/typescript/review-checklist/re-renders-and-memoization.md` |
| Error handling | `knowledge/typescript/review-checklist/error-handling.md` |
| Async and concurrency | `knowledge/typescript/review-checklist/async-and-concurrency.md` |
| Node backend | `knowledge/typescript/review-checklist/node-backend.md` |
| Module and API hygiene | `knowledge/typescript/review-checklist/module-and-api-hygiene.md` |
