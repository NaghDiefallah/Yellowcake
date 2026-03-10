using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class DownloadQueue
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSemaphores = new();
    private readonly ConcurrentDictionary<string, DownloadTask> _activeTasks = new();
    private readonly ConcurrentDictionary<string, DownloadTask> _pendingTasks = new();
    private readonly int _maxParallel;
    private readonly int _maxPerHost;
    private long _totalBytesDownloaded;
    private long _estimatedTotalBytes;

    public int MaxParallelDownloads => _maxParallel;
    public int ActiveDownloads => _activeTasks.Count;
    public int QueuedDownloads => _pendingTasks.Count;
    public double Progress => _estimatedTotalBytes > 0
        ? (double)_totalBytesDownloaded / _estimatedTotalBytes * 100
        : 0;

    public event EventHandler<DownloadQueueEventArgs>? DownloadStarted;
    public event EventHandler<DownloadQueueEventArgs>? DownloadCompleted;
    public event EventHandler<DownloadQueueEventArgs>? DownloadFailed;
    public event EventHandler? QueueChanged;

    public DownloadQueue(int maxParallel = 4, int maxPerHost = 2)
    {
        _maxParallel = maxParallel;
        _maxPerHost = Math.Max(1, maxPerHost);
        _semaphore = new SemaphoreSlim(maxParallel, maxParallel);
    }

    public async Task<T> EnqueueAsync<T>(
        string taskId,
        Func<IProgress<double>, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        long estimatedBytes = 0,
        int priority = 0,
        Uri? resourceUri = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var task = new DownloadTask
        {
            Id = taskId,
            EstimatedBytes = estimatedBytes,
            Priority = priority,
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        _pendingTasks[taskId] = task;
        Interlocked.Add(ref _estimatedTotalBytes, estimatedBytes);
        QueueChanged?.Invoke(this, EventArgs.Empty);

        var acquiredGlobal = false;
        SemaphoreSlim? hostSemaphore = null;

        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            acquiredGlobal = true;

            if (resourceUri?.Host is { Length: > 0 } host)
            {
                hostSemaphore = _hostSemaphores.GetOrAdd(host, _ => new SemaphoreSlim(_maxPerHost, _maxPerHost));
                await hostSemaphore.WaitAsync(cancellationToken);
            }

            _pendingTasks.TryRemove(taskId, out _);
            _activeTasks[taskId] = task;
            DownloadStarted?.Invoke(this, new DownloadQueueEventArgs { TaskId = taskId });

            var progress = new Progress<double>(p =>
            {
                task.Progress = p;
                var downloaded = (long)(estimatedBytes * p / 100);
                var delta = downloaded - task.BytesDownloaded;
                Interlocked.Add(ref _totalBytesDownloaded, delta);
                task.BytesDownloaded = downloaded;
            });

            var result = await operation(progress, task.CancellationTokenSource.Token);

            DownloadCompleted?.Invoke(this, new DownloadQueueEventArgs { TaskId = taskId });
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download task {TaskId} failed", taskId);
            DownloadFailed?.Invoke(this, new DownloadQueueEventArgs
            {
                TaskId = taskId,
                Error = ex.Message
            });
            throw;
        }
        finally
        {
            _pendingTasks.TryRemove(taskId, out _);
            _activeTasks.TryRemove(taskId, out _);

            var remainingEstimated = Interlocked.Add(ref _estimatedTotalBytes, -task.EstimatedBytes);
            if (remainingEstimated < 0)
            {
                Interlocked.Exchange(ref _estimatedTotalBytes, 0);
            }

            var remainingDownloaded = Interlocked.Add(ref _totalBytesDownloaded, -task.BytesDownloaded);
            if (remainingDownloaded < 0)
            {
                Interlocked.Exchange(ref _totalBytesDownloaded, 0);
            }

            try
            {
                task.CancellationTokenSource?.Dispose();
            }
            catch
            {
            }

            if (hostSemaphore != null)
            {
                hostSemaphore.Release();
            }

            if (acquiredGlobal)
            {
                _semaphore.Release();
            }

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool PauseTask(string taskId)
    {
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.IsPaused = true;
            return true;
        }
        return false;
    }

    public bool ResumeTask(string taskId)
    {
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.IsPaused = false;
            return true;
        }
        return false;
    }

    public bool CancelTask(string taskId)
    {
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.CancellationTokenSource?.Cancel();
            return true;
        }
        return false;
    }

    public List<DownloadTask> GetActiveTasks() => _activeTasks.Values.ToList();
    public List<DownloadTask> GetPendingTasks() => _pendingTasks.Values.ToList();

    public void ClearCompleted()
    {
        var completed = _activeTasks.Where(t => t.Value.Progress >= 100).ToList();
        foreach (var task in completed)
        {
            _activeTasks.TryRemove(task.Key, out _);
        }
    }
}

public class DownloadTask
{
    public string Id { get; set; } = string.Empty;
    public double Progress { get; set; }
    public long EstimatedBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public int Priority { get; set; }
    public bool IsPaused { get; set; }
    public CancellationTokenSource? CancellationTokenSource { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public TimeSpan EstimatedTimeRemaining => CalculateETA();

    private TimeSpan CalculateETA()
    {
        if (Progress <= 0) return TimeSpan.Zero;

        var elapsed = DateTime.Now - StartTime;
        var remainingPercent = 100 - Progress;
        var estimatedTotal = elapsed.TotalSeconds * (100 / Progress);
        return TimeSpan.FromSeconds(estimatedTotal - elapsed.TotalSeconds);
    }
}

public class DownloadQueueEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string? Error { get; set; }
}