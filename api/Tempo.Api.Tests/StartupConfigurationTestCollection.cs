using Xunit;

namespace Tempo.Api.Tests;

/// <summary>
/// Test collection for startup configuration tests to ensure isolation
/// This prevents environment variables set by other tests (like TempoWebApplicationFactory)
/// from affecting these tests
/// </summary>
[CollectionDefinition("Startup Configuration Tests")]
public class StartupConfigurationTestCollection : ICollectionFixture<object>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // [Collection] attributes can be bound to it.
}
