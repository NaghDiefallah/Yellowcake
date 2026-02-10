using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class PerformanceDashboardViewModel : ObservableObject
{
    private readonly PerformanceTracker _tracker;

    [ObservableProperty] private Services.PerformanceStats _stats;

    public PerformanceDashboardViewModel()
    {
        // Create a new tracker instance
        _tracker = new PerformanceTracker(new DatabaseService());
        _stats = _tracker.GetStats();

        // Add ranking to top mods
        int rank = 1;
        foreach (var mod in _stats.TopMods)
        {
            mod.Rank = rank++;
            mod.PercentOfMax = _stats.TopMods.Any() 
                ? (double)mod.Count / _stats.TopMods.Max(m => m.Count) * 100 
                : 0;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Stats = _tracker.GetStats();
        
        // Update rankings
        int rank = 1;
        foreach (var mod in Stats.TopMods)
        {
            mod.Rank = rank++;
            mod.PercentOfMax = Stats.TopMods.Any() 
                ? (double)mod.Count / Stats.TopMods.Max(m => m.Count) * 100 
                : 0;
        }
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        var result = await NotificationService.Instance.ConfirmAsync(
            "Clear History",
            "Are you sure you want to clear all performance history? This cannot be undone.",
            TimeSpan.FromSeconds(30)
        );

        if (result)
        {
            _tracker.ClearHistory();
            Refresh();
            NotificationService.Instance.Success("Performance history cleared");
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportPath = Path.Combine(Path.GetTempPath(), $"yellowcake_performance_{timestamp}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Mod ID,Mod Name,Size (MB),Duration (s),Speed (MB/s),Success");

            foreach (var metric in Stats.RecentDownloads)
            {
                sb.AppendLine($"{metric.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                             $"{metric.ModId}," +
                             $"\"{metric.ModName}\"," +
                             $"{metric.BytesDownloadedMB:F2}," +
                             $"{metric.Duration.TotalSeconds:F1}," +
                             $"{metric.SpeedMBps:F2}," +
                             $"{metric.Success}");
            }

            await File.WriteAllTextAsync(exportPath, sb.ToString());

            NotificationService.Instance.Success($"Performance data exported to {exportPath}");
            Log.Information("Exported performance data to {Path}", exportPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export performance data");
            NotificationService.Instance.Error("Failed to export performance data");
        }
    }
}