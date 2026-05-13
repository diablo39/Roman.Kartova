using Microsoft.VisualStudio.TestTools.UnitTesting;

// Integration tests mutate process-global env vars (ConnectionStrings__*,
// Authentication__*, Cors__*) when the WebApplicationFactory boots.
// Disable parallel test-class execution so concurrent classes cannot clobber each other.
[assembly: DoNotParallelize]
