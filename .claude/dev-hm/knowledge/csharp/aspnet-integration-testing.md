# ASP.NET Core integration testing

In-process integration tests for ASP.NET Core services: boot the real app with
`WebApplicationFactory<TEntryPoint>`, hit it over an in-memory `HttpClient`, swap only the edges
(database, external HTTP, authentication). Framework and package versions are in
`knowledge/shared/versions.md`; unit-level conventions (MSTest, NSubstitute, Testcontainers
fixtures) are in `knowledge/csharp/testing.md`. These tests sit between the fast unit tier and
end-to-end tests: real routing, middleware, filters, model binding, DI wiring, and serialization —
without a network socket or a deployed environment.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| WebApplicationFactory basics {#waf} | `knowledge/csharp/aspnet-integration-testing/waf.md` |
| Customizing the host {#customize} | `knowledge/csharp/aspnet-integration-testing/customize.md` |
| Real database, not fakes {#database} | `knowledge/csharp/aspnet-integration-testing/database.md` |
| Real authentication — no fake auth handler {#auth} | `knowledge/csharp/aspnet-integration-testing/auth.md` |
| What to assert {#assertions} | `knowledge/csharp/aspnet-integration-testing/assertions.md` |
| Running and coverage {#commands} | `knowledge/csharp/aspnet-integration-testing/commands.md` |
| gRPC services {#grpc} | `knowledge/csharp/aspnet-integration-testing/grpc.md` |
