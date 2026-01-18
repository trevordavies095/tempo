using Xunit;

// Configure xUnit to run tests sequentially (no parallelization)
// This prevents database state conflicts in integration tests
// Trade-off: ~2-3x slower execution, but eliminates race conditions
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
