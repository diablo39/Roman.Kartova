using Xunit;

// KartovaApiFixture and KartovaApiFaultInjectionFixture each spin up their own
// Postgres testcontainer + WebApplicationFactory and write the resulting
// connection strings to process-global environment variables (Program.cs reads
// them via WebApplication.CreateBuilder). When xUnit runs the two collections
// in parallel, fixture B's env-var write races fixture A's host build and the
// already-built NpgsqlDataSource ends up pointing at the wrong port, surfacing
// as intermittent "database unavailable or connection failure" on tests that
// happened to bind during the race.
//
// Disabling parallel collection execution serializes the fixtures end-to-end,
// keeping their env-var manipulation isolated. Single-threaded integration is
// already the dominant cost (Testcontainers Postgres start is ~3-5s) so the
// performance impact is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
