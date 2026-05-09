using Microsoft.VisualStudio.TestTools.UnitTesting;

// Preserves the env-var-race protection that xUnit's [Collection] previously provided —
// integration tests touch ConnectionStrings__* and Authentication__* env vars that are
// process-global. Running classes in parallel would clobber each other's state.
[assembly: DoNotParallelize]
