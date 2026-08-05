# C# testing — Coverage and commands

Section of `knowledge/csharp/testing.md`.


- Collect coverage with the `coverlet.collector` package plus a runsettings file:
  `dotnet test <solution> --settings coverlet.runsettings --collect:"XPlat Code Coverage"`.
  Render locally with ReportGenerator. (The MTP-native `--coverage` flag applies only to projects
  that opt into the Microsoft Testing Platform runner — these do not.)
- Coverage is a floor, not a target — see `knowledge/quality/test-strategy.md`. Prioritize branch
  coverage of business logic and error paths over line coverage of DTOs. Types that are pure data
  carriers or composition roots carry `[ExcludeFromCodeCoverage]` rather than being tested for the
  sake of the number.
- Keep unit tests (no I/O, milliseconds) separate from Testcontainers integration tests (separate
  projects) so the fast suite gates every change and the slow suite runs in CI.
- Test behavior through the public surface, not private methods; a test that only passes because it
  mirrors the implementation locks in that implementation.
