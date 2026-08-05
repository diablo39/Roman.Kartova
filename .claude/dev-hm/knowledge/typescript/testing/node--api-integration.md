# TypeScript testing: Vitest, RTL, MSW, Playwright — Node / API integration

Section of `knowledge/typescript/testing.md`.


- Test HTTP handlers through the app (Supertest against the Express/Fastify instance, or the
  framework's request helper) rather than calling controller functions directly — middleware,
  validation, and status mapping are part of the behaviour.
- Use Testcontainers for a real database/broker in integration tests instead of mocking the driver;
  see `knowledge/quality/test-strategy.md`. Reset state between tests (transaction rollback or
  truncate), never share mutable rows across tests.
