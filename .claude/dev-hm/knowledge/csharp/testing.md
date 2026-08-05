# C# testing

Stack: MSTest v4 as the test framework, its native assertions, NSubstitute for test doubles,
Testcontainers for real infrastructure in integration tests, and coverlet for coverage. Do not add
a fluent-assertion library. Versions are in `knowledge/shared/versions.md`. API-level integration
testing with `WebApplicationFactory` — in-process host, auth, real database wiring — is
`knowledge/csharp/aspnet-integration-testing.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| MSTest essentials | `knowledge/csharp/testing/mstest-essentials.md` |
| NSubstitute {#nsubstitute} | `knowledge/csharp/testing/nsubstitute.md` |
| Testcontainers integration tests {#testcontainers} | `knowledge/csharp/testing/testcontainers.md` |
| Source-generator testing {#source-generator-testing} | `knowledge/csharp/testing/source-generator-testing.md` |
| Coverage and commands | `knowledge/csharp/testing/coverage-and-commands.md` |
