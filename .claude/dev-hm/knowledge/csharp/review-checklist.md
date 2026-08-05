# C# review checklist

A severity-tiered catalogue for reviewing C#/.NET changes. Severities S0–S3 are defined in
`knowledge/shared/severity-tiers.md` (S0 Block, S1 Must-fix, S2 Should-fix, S3 Advisory). Deterministic
checks with observable pass criteria live in the oracle addendum `oracles/addenda/csharp.md`
(SEC-CS-*, QUA-CS-*); re-run those independently and report per-ID verdicts. This checklist covers
those plus the judgement-based findings an oracle cannot express. Cite every finding as
`file.cs:line`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| How to use | `knowledge/csharp/review-checklist/how-to-use.md` |
| Security {#security} | `knowledge/csharp/review-checklist/security.md` |
| Async and concurrency {#async} | `knowledge/csharp/review-checklist/async.md` |
| Resource management {#resource-management} | `knowledge/csharp/review-checklist/resource-management.md` |
| Correctness and design | `knowledge/csharp/review-checklist/correctness-and-design.md` |
| Data access {#data-access} | `knowledge/csharp/review-checklist/data-access.md` |
| Source generators over reflection {#source-generators} | `knowledge/csharp/review-checklist/source-generators.md` |
| Nullability {#nullability} | `knowledge/csharp/review-checklist/nullability.md` |
| Secrets {#secrets} | `knowledge/csharp/review-checklist/secrets.md` |
| Deserialization {#deserialization} | `knowledge/csharp/review-checklist/deserialization.md` |
| Crypto {#crypto} | `knowledge/csharp/review-checklist/crypto.md` |
| Error handling {#error-handling} | `knowledge/csharp/review-checklist/error-handling.md` |
