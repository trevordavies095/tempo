using FluentAssertions;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Services;

public class IntervalsIcuSyncQueueTests
{
    [Fact]
    public void TryWake_CoalescesWhilePendingOrInFlight()
    {
        var queue = new IntervalsIcuSyncQueue();

        queue.TryWake().Should().BeTrue();
        queue.TryWake().Should().BeFalse();

        queue.BeginTick();
        queue.TryWake().Should().BeFalse();

        queue.EndTick();
        queue.TryWake().Should().BeTrue();
    }

    [Fact]
    public void EndTick_ClearsPendingEvenIfBeginTickNeverRan()
    {
        var queue = new IntervalsIcuSyncQueue();

        queue.TryWake().Should().BeTrue();
        // Simulate a consumer that failed before BeginTick (historical stuck-wake bug).
        queue.EndTick();

        queue.TryWake().Should().BeTrue();
    }
}
