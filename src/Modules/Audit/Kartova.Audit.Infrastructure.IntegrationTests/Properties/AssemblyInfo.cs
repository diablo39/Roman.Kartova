using Microsoft.VisualStudio.TestTools.UnitTesting;

// Integration tests spin up a Testcontainers Postgres instance via AuditLogFixture.
// Disable parallel test-class execution so ClassInitialize/ClassCleanup lifecycle
// is not re-entered concurrently and container startup/teardown is deterministic.
[assembly: DoNotParallelize]
