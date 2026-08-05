# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Data access: a probe value round-trips as literal data

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our data-access layer binds values as parameters, so a value with quote, comment, or separator
characters is stored, filtered on, and retrieved unchanged — touching nothing else. Run against
the production engine in an ephemeral container; an in-memory lookalike quotes differently.

```python
@pytest.mark.control
@pytest.mark.parametrize("probe", DATA_PROBES)
def test_value_round_trips_as_literal_data(repo, probe, db):
    created = repo.create_customer(name=probe)
    assert repo.get_customer(created.id).name == probe       # stored uninterpreted
    assert repo.find_customers(name=probe) == [created]      # filtering binds the value too
    assert db.scalar("select count(*) from customers") == 1  # nothing else was touched
```

```java
@Test @Tag("control")
void probeValuesRoundTripAsLiteralData() {
    for (var probe : DataProbes.ALL) {
        var id = customers.create(probe);
        assertThat(customers.findById(id).orElseThrow().name()).isEqualTo(probe);
        assertThat(customers.findByName(probe)).extracting(Customer::id).containsExactly(id);
    }
    assertThat(customers.count()).isEqualTo(DataProbes.ALL.size());
}
```

The test drives the repository API, not the driver, so it covers our query construction:

| Ecosystem | Parameter binding under test |
|---|---|
| TypeScript | placeholder parameters in pg/knex or the query builder's bound values; Testcontainers for the engine |
| Python | driver placeholders or SQLAlchemy bound parameters |
| Java | `JdbcTemplate`/JPA named parameters; Testcontainers |
| C# | Dapper/EF Core parameters; Testcontainers |
| Rust | sqlx compile-checked queries with bound arguments |
| C++ | prepared statements (`PQexecParams` and equivalents) |
| Flutter / Dart | `?` placeholders in sqflite/drift against the on-device engine |

The fails-when-removed lever: swap one bound parameter for string concatenation in a scratch
run — the round-trip or the count assertion fails on the quote probes.
