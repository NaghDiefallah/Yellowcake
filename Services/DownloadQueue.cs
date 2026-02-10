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
    private readonly ConcurrentDictionary<string, DownloadTask> _activeTasks = new();
    private readonly ConcurrentQueue<DownloadTask> _pendingTasks = new();
    private readonly int _maxParallel;
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

    public DownloadQueue(int maxParallel = 4)
    {
        _maxParallel = maxParallel;
        _semaphore = new SemaphoreSlim(maxParallel, maxParallel);
    }

    public async Task<T> EnqueueAsync<T>(
        string taskId,
        Func<IProgress<double>, CancellationToken, Task<T>> operation,
        long estimatedBytes = 0,
        int priority = 0)
    {
        var task = new DownloadTask
        {
            Id = taskId,
            EstimatedBytes = estimatedBytes,
            Priority = priority
        };

        _pendingTasks.Enqueue(task);
        Interlocked.Add(ref _estimatedTotalBytes, estimatedBytes);
        QueueChanged?.Invoke(this, EventArgs.Empty);

        await _semaphore.WaitAsync();

        try
        {
            _activeTasks[taskId] = task;
            DownloadStarted?.Invoke(this, new DownloadQueueEventArgs { TaskId = taskId });

            var progress = new Progress<double>(p =>
            {
                task.Progress = p;
                var downloaded = (long)(estimatedBytes * p / 100);
                Interlocked.Exchange(ref _totalBytesDownloaded,
                    _totalBytesDownloaded - task.BytesDownloaded + downloaded);
                task.BytesDownloaded = downloaded;
            });

            var result = await operation(progress, CancellationToken.None);

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
            _activeTasks.TryRemove(taskId, out _);
            _semaphore.Release();
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
    public List<DownloadTask> GetPendingTasks() => _pendingTasks.ToList();

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