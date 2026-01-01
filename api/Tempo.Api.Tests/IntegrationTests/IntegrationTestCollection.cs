using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Test collection for integration tests to ensure sequential execution
/// This prevents tests from different classes from running in parallel and interfering with each other's data
/// </summary>
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<object>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // [Collection] attributes can be bound to it.
}

