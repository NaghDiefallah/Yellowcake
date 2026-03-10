using FluentAssertions;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class PerformanceTrackerTests
{
    [Fact]
    public void GetStats_ShouldCalculateCorrectPercentiles()
    {
        var db = new DatabaseService(":memory:");
        var sut = new PerformanceTracker(db);

        // Record 5 operations with known durations: 10, 20, 30, 40, 50 ms
        // CalculatePercentile uses: sorted[ceil(p * count) - 1]
        //   p50: ceil(0.5 * 5) - 1 = 3 - 1 = 2  → sorted[2] = 30
        //   p95: ceil(0.95 * 5) - 1 = 5 - 1 = 4  → sorted[4] = 50
        foreach (var ms in new[] { 50, 10, 30, 20, 40 })
        {
            sut.RecordOperation("test-op", TimeSpan.FromMilliseconds(ms), success: true);
        }

        var stats = sut.GetStats();

        stats.OperationP50Ms.Should().BeApproximately(30, 0.01);
        stats.OperationP95Ms.Should().BeApproximately(50, 0.01);
    }

    [Fact]
    public void GetStats_ShouldReturnZeroPercentilesWhenNoOperationsRecorded()
    {
        var db = new DatabaseService(":memory:");
        var sut = new PerformanceTracker(db);

        var stats = sut.GetStats();

        stats.OperationP50Ms.Should().Be(0);
        stats.OperationP95Ms.Should().Be(0);
    }
}
