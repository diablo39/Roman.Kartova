# EF Core patterns — Testing data access {#testing}

Section of `knowledge/csharp/efcore.md`.


Test repositories and query logic against the real database engine in a Testcontainers container —
`knowledge/csharp/testing.md#testcontainers` has the fixture pattern. Apply the real migrations to
the container, not `EnsureCreated`, so the tests also validate the migration chain.

The `Microsoft.EntityFrameworkCore.InMemory` provider is not a relational database: no
transactions, no constraint enforcement, different query translation — tests that pass on it prove
little. Legacy note: keep it only where an existing suite already depends on it and containerizing
is genuinely impractical; SQLite-in-memory is a closer-but-still-different stand-in with the same
caveat. New suites use the real engine.
