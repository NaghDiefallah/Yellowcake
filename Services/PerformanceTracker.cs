using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class PerformanceTracker
{
    private readonly DatabaseService _db;
    private readonly List<DownloadMetric> _recentDownloads = new();
    private const int MaxRecentDownloads = 100;

    public PerformanceTracker(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public void RecordDownload(string modId, string modName, long bytes, TimeSpan duration, bool success)
    {
        var metric = new DownloadMetric
        {
            Timestamp = DateTime.UtcNow,
            ModId = modId,
            ModName = modName,
            BytesDownloaded = bytes,
            Duration = duration,
            Success = success,
            SpeedMBps = bytes / duration.TotalSeconds / 1_048_576
        };

        lock (_recentDownloads)
        {
            _recentDownloads.Add(metric);
            if (_recentDownloads.Count > MaxRecentDownloads)
            {
                _recentDownloads.RemoveAt(0);
            }
        }

        // Persist to database
        try
        {
            _db.Upsert("performance_metrics", metric);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist performance metric");
        }
    }

    public PerformanceStats GetStats()
    {
        var allMetrics = _db.GetAll<DownloadMetric>("performance_metrics");
        var successful = allMetrics.Where(m => m.Success).ToList();

        return new PerformanceStats
        {
            TotalDownloads = allMetrics.Count,
            SuccessfulDownloads = successful.Count,
            FailedDownloads = allMetrics.Count - successful.Count,
            TotalDataDownloadedMB = successful.Sum(m => m.BytesDownloaded) / 1_048_576.0,
            AverageSpeedMBps = successful.Any() ? successful.Average(m => m.SpeedMBps) : 0,
            FastestSpeedMBps = successful.Any() ? successful.Max(m => m.SpeedMBps) : 0,
            TotalDownloadTime = TimeSpan.FromSeconds(successful.Sum(m => m.Duration.TotalSeconds)),
            RecentDownloads = _recentDownloads.TakeLast(20).Reverse().ToList(),
            DownloadsByDay = GetDownloadsByDay(allMetrics),
            TopMods = GetTopMods(successful)
        };
    }

    private Dictionary<DateTime, int> GetDownloadsByDay(List<DownloadMetric> metrics)
    {
        return metrics
            .GroupBy(m => m.Timestamp.Date)
            .OrderByDescending(g => g.Key)
            .Take(30)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private List<ModDownloadCount> GetTopMods(List<DownloadMetric> metrics)
    {
        return metrics
            .GroupBy(m => new { m.ModId, m.ModName })
            .Select(g => new ModDownloadCount
            {
                ModId = g.Key.ModId,
                ModName = g.Key.ModName,
                Count = g.Count(),
                TotalMB = g.Sum(m => m.BytesDownloaded) / 1_048_576.0
            })
            .OrderByDescending(m => m.Count)
            .Take(10)
            .ToList();
    }

    public void ClearHistory()
    {
        _db.DeleteAll("performance_metrics");
        lock (_recentDownloads)
        {
            _recentDownloads.Clear();
        }
    }
}

public class DownloadMetric
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; }
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public double SpeedMBps { get; set; }
    
    public double BytesDownloadedMB => BytesDownloaded / 1_048_576.0;
}

public class PerformanceStats
{
    public int TotalDownloads { get; set; }
    public int SuccessfulDownloads { get; set; }
    public int FailedDownloads { get; set; }
    public double TotalDataDownloadedMB { get; set; }
    public double AverageSpeedMBps { get; set; }
    public double FastestSpeedMBps { get; set; }
    public TimeSpan TotalDownloadTime { get; set; }
    public List<DownloadMetric> RecentDownloads { get; set; } = new();
    public Dictionary<DateTime, int> DownloadsByDay { get; set; } = new();
    public List<ModDownloadCount> TopMods { get; set; } = new();
    
    public double SuccessRate => TotalDownloads > 0 
        ? (double)SuccessfulDownloads / TotalDownloads * 100 
        : 0;
    
    public double TotalDataDownloadedGB => TotalDataDownloadedMB / 1024.0;
}

public class ModDownloadCount
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public int Count { get; set; }
    public double TotalMB { get; set; }
    public int Rank { get; set; }
    public double PercentOfMax { get; set; }
}