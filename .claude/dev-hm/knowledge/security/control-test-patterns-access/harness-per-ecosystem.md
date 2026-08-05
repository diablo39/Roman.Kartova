# Control-test patterns: transport, authentication, session, authorization — Harness per ecosystem

Section of `knowledge/security/control-test-patterns-access.md`.


Boundary-level control tests drive the application in-process where the stack allows it:

| Ecosystem | Harness for boundary control tests |
|---|---|
| TypeScript | Vitest/Jest + supertest against the app instance (or Fastify `inject`) |
| Python | pytest + the ASGI test client (FastAPI/Starlette `TestClient`, httpx ASGI transport) |
| Java | JUnit + Spring `MockMvc`/`WebTestClient`; Testcontainers for real engines |
| C# | MSTest + `WebApplicationFactory<TProgram>` |
| Rust | cargo test + `tower::ServiceExt::oneshot` on the axum router |
| C++ | GoogleTest/GoogleMock (Catch2 secondary) + in-process server on an ephemeral port, driven by libcurl |
| Flutter / Dart | `flutter_test`/`package:test`; client-side controls exercised against a local test server started by the fixture |
