# ASP.NET Core integration testing — What to assert {#assertions}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


- Status code and problem-details shape for every error path — no stack traces or internal detail
  in response bodies.
- Response DTOs deserialized with the app's own JSON options (grab `JsonSerializerOptions` from
  `factory.Services`), not a hand-rolled parallel contract.
- Side effects through the seams: rows in the container database, messages captured by the
  recording fake, emitted spans/metrics via an in-memory exporter
  (`knowledge/csharp/observability.md#testing`).
- For endpoint handlers with real logic, prefer unit tests on the handler/service and keep the
  integration test to one happy path plus the auth/validation negatives — the pyramid still
  applies (`knowledge/quality/test-strategy.md`).
