using Microsoft.VisualStudio.TestTools.UnitTesting;

// Integration tests mutate process-global env vars (ConnectionStrings__*, Authentication__*).
// Disable parallel test-class execution so concurrent classes cannot clobber each other.
[assembly: DoNotParallelize]
