# C# oracle addendum

Language-specific, deterministic checks for C#/.NET changes. Cross-language checks live in
`oracles/security-oracle.md` (SEC-*) and `oracles/quality-oracle.md` (QUA-*); this file adds only
C#-specific entries whose pass criteria are decidable from the code and project files alone. IDs are
stable and never reused after removal. Where a core entry and an addendum entry cover the same
defect, the stricter severity and verdict govern.

## Security (SEC-CS)

| ID | Check | Pass criterion | Severity | Remediation |
|---|---|---|---|---|
| SEC-CS-001 | SQL/EF parameterization | Every database command uses parameters or LINQ; zero string-concatenated or interpolated SQL containing a non-constant value, including EF `FromSqlRaw`/`ExecuteSqlRaw` with concatenated input (`FromSqlInterpolated` is compliant). | S0 | knowledge/csharp/review-checklist/data-access.md |
| SEC-CS-002 | No secrets in literals | No string literal holds a credentialed connection string, API key, password, bearer/JWT token, or private key; such values are read from a configuration or secret provider. | S0 | knowledge/csharp/review-checklist/secrets.md |
| SEC-CS-003 | Safe deserialization | No reference to `BinaryFormatter`, `NetDataContractSerializer`, `SoapFormatter`, or `LosFormatter`; JSON polymorphic type handling is not enabled for untrusted input (no `TypeNameHandling` other than `None`; no unrestricted polymorphic resolver). | S0 | knowledge/csharp/review-checklist/deserialization.md |
| SEC-CS-004 | Strong cryptography | No `MD5`/`SHA1`/`DES`/`TripleDES`/`RC2` for a security purpose and no `CipherMode.ECB`; secret/token/salt randomness uses `RandomNumberGenerator`, not `System.Random`/`Random.Shared`. | S1 | knowledge/csharp/review-checklist/crypto.md |
| SEC-CS-005 | Path traversal guarded | Any filesystem path derived from external input is canonicalized (`Path.GetFullPath`) and verified to remain under an allowed base directory before any I/O. | S0 | knowledge/csharp/review-checklist/security.md |
| SEC-CS-006 | TLS validation intact | No certificate-validation callback unconditionally returns `true` (`ServerCertificateCustomValidationCallback`, `RemoteCertificateValidationCallback`), and validation is not otherwise disabled on the handler/`ServicePointManager`. | S0 | knowledge/csharp/review-checklist/security.md |

## Quality (QUA-CS)

| ID | Check | Pass criterion | Severity | Remediation |
|---|---|---|---|---|
| QUA-CS-001 | Nullable reference types enabled | Every changed project resolves `<Nullable>enable</Nullable>` (directly or via `Directory.Build.props`) and the diff adds no `#nullable disable`. | S1 | knowledge/csharp/review-checklist/nullability.md |
| QUA-CS-002 | JSON via source generator | On projects with `PublishAot`/`IsAotCompatible` true, every `JsonSerializer.Serialize`/`Deserialize` call passes a `JsonTypeInfo`/`JsonSerializerContext` (no reflection-mode overload). | S2 | knowledge/csharp/source-generators.md#json |
| QUA-CS-003 | Logging via templates/LoggerMessage | No `ILogger` call passes an interpolated (`$"..."`) or concatenated string as the message; messages are constant templates with named placeholders or `[LoggerMessage]` methods. | S2 | knowledge/csharp/source-generators.md#logging |
| QUA-CS-004 | Regex via GeneratedRegex | No `new Regex(pattern)` where `pattern` is a compile-time-constant string; constant patterns use `[GeneratedRegex]` partial methods. | S2 | knowledge/csharp/source-generators.md#regex |
| QUA-CS-005 | No async void | No `async void` method except an event handler whose signature matches a subscribed event delegate (e.g. `(object?, EventArgs)`). | S1 | knowledge/csharp/review-checklist/async.md |
| QUA-CS-006 | No sync-over-async | No `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` on a `Task`/`ValueTask` outside an application entry point or a comment-documented top-level sync boundary. | S1 | knowledge/csharp/review-checklist/async.md |
| QUA-CS-007 | CancellationToken propagation | Each public async method declares a `CancellationToken` parameter and forwards it to every awaited downstream call that exposes a token-accepting overload. | S2 | knowledge/csharp/review-checklist/async.md |
| QUA-CS-008 | ConfigureAwait consistency | Within one library (non-ASP.NET, non-entry) assembly, awaits are uniform: either all use `ConfigureAwait(false)` or none do — no mix in the changed files. | S2 | knowledge/csharp/review-checklist/async.md |
| QUA-CS-009 | Disposable correctness | A type holding an `IDisposable`/`IAsyncDisposable` field implements the matching dispose interface and releases the field; a disposable local is wrapped in `using`/`await using` or disposed on all paths. | S1 | knowledge/csharp/review-checklist/resource-management.md |
| QUA-CS-010 | No per-call HttpClient | No `new HttpClient()` constructed per request or method invocation; HTTP access goes through `IHttpClientFactory` or a typed client (a documented long-lived singleton is compliant). | S1 | knowledge/csharp/review-checklist/resource-management.md |
