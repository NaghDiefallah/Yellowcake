using FluentAssertions;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class DownloadQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldTrackQueuedAndActiveDownloads()
    {
        var queue = new DownloadQueue(maxParallel: 1, maxPerHost: 1);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.EnqueueAsync(
            taskId: "first",
            operation: async (progress, _) =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
                progress.Report(100);
                return true;
            },
            cancellationToken: CancellationToken.None,
            estimatedBytes: 100,
            resourceUri: new Uri("https://example.test/a"));

        await firstStarted.Task;

        var second = queue.EnqueueAsync(
            taskId: "second",
            operation: (progress, _) =>
            {
                progress.Report(100);
                return Task.FromResult(true);
            },
            cancellationToken: CancellationToken.None,
            estimatedBytes: 100,
            resourceUri: new Uri("https://example.test/b"));

        await Task.Delay(100);

        queue.ActiveDownloads.Should().Be(1);
        queue.QueuedDownloads.Should().Be(1);

        releaseFirst.TrySetResult(true);
        await Task.WhenAll(first, second);

        queue.ActiveDownloads.Should().Be(0);
        queue.QueuedDownloads.Should().Be(0);
        queue.Progress.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public async Task EnqueueAsync_WhenCanceledWhileQueued_ShouldCleanupPendingState()
    {
        var queue = new DownloadQueue(maxParallel: 1, maxPerHost: 1);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.EnqueueAsync(
            taskId: "first",
            operation: async (_, _) =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
                return true;
            },
            cancellationToken: CancellationToken.None,
            estimatedBytes: 100,
            resourceUri: new Uri("https://example.test/a"));

        await firstStarted.Task;

        using var queuedCts = new CancellationTokenSource();
        var queued = queue.EnqueueAsync(
            taskId: "queued",
            operation: (_, _) => Task.FromResult(true),
            cancellationToken: queuedCts.Token,
            estimatedBytes: 100,
            resourceUri: new Uri("https://example.test/b"));

        await Task.Delay(100);
        queue.QueuedDownloads.Should().Be(1);

        queuedCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => queued);

        releaseFirst.TrySetResult(true);
        await first;

        queue.ActiveDownloads.Should().Be(0);
        queue.QueuedDownloads.Should().Be(0);
        queue.Progress.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public async Task EnqueueAsync_WhenCanceledWhileActive_ShouldCleanupAndThrow()
    {
        var queue = new DownloadQueue(maxParallel: 1, maxPerHost: 1);
        var taskStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activeCts = new CancellationTokenSource();

        var active = queue.EnqueueAsync(
            taskId: "active",
            operation: async (_, ct) =>
            {
                taskStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            cancellationToken: activeCts.Token,
            estimatedBytes: 100,
            resourceUri: new Uri("https://example.test/a"));

        await taskStarted.Task;
        queue.ActiveDownloads.Should().Be(1);

        activeCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);

        queue.ActiveDownloads.Should().Be(0);
        queue.QueuedDownloads.Should().Be(0);
        queue.Progress.Should().BeApproximately(0, 0.01);
    }
}
