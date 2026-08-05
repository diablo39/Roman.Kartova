# ASP.NET Core integration testing — Running and coverage {#commands}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


Integration test projects run on the VSTest runner like the unit tier
(`knowledge/csharp/testing.md` for the project setup):

- Whole tier: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj`
- Filter within a run (VSTest `--filter` expressions):
  `dotnet test --filter "TestCategory=integration"`, `FullyQualifiedName~OrderEndpoint` for a
  single class or method, `&`/`|` to combine.
- Coverage: `dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"`,
  rendered with ReportGenerator.

Keep the integration tier out of the default fast loop: separate project (preferred) or a
`[TestCategory]` the fast run excludes. In CI the tier runs on every merge with containers started fresh —
`WithReuse(true)` stays a local-only speed-up.
