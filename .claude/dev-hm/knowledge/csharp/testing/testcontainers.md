# C# testing — Testcontainers integration tests {#testcontainers}

Section of `knowledge/csharp/testing.md`.


Run integration tests against the real dependency (PostgreSQL, Kafka, Keycloak) in a throwaway
container instead of mocks or in-memory fakes. MSTest has no fixture-injection concept, so the
shared container lives in a static assembly-scoped holder:

```csharp
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static PostgreSqlContainer Postgres { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Postgres = new PostgreSqlBuilder()
            .WithImage(PostgresImage)   // one shared constant; current major per knowledge/shared/versions.md
            .Build();
        await Postgres.StartAsync();
    }

    // MSTest only invokes [AssemblyCleanup] if [AssemblyInitialize] completed.
    [AssemblyCleanup]
    public static async Task CleanupAsync() => await Postgres.DisposeAsync();
}
```

```csharp
[TestClass]
public sealed class OrderRepositoryTests
{
    [TestMethod]
    public async Task Insert_then_get_round_trips()
    {
        await using var conn = new NpgsqlConnection(
            IntegrationTestAssemblySetup.Postgres.GetConnectionString());
        // exercise the repository against the live database
    }
}
```

Container reuse and lifecycle:

- One container per assembly, started in `[AssemblyInitialize]`. Starting a container per test is
  slow and flaky; per class is usually still too slow once migrations run.
- Pair the shared fixture with `[assembly: DoNotParallelize]` so the single instance is never
  accessed concurrently. If you do enable parallelism, give each parallel scope its own container,
  schema, or database to avoid cross-talk.
- `WithReuse(true)` keeps the container alive between runs (matched by a config hash), cutting local
  startup from tens of seconds to under a second. It disables the resource reaper. Enable it only
  for local development; a CI run wants a clean, disposable environment, so gate it on an env var.
  Between reused runs, reset state (truncate tables) so tests stay independent.
