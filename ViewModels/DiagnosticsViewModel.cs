using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    [ObservableProperty] private string _appMemoryUsage = string.Empty;
    [ObservableProperty] private string _cacheSize = string.Empty;
    [ObservableProperty] private string _databaseSize = string.Empty;
    [ObservableProperty] private ObservableCollection<AssemblyInfo> _loadedAssemblies = new();
    [ObservableProperty] private ObservableCollection<PerformanceMetric> _performanceMetrics = new();
    [ObservableProperty] private string _systemInfo = string.Empty;

    public DiagnosticsViewModel(DatabaseService db)
    {
        _db = db;
        LoadDiagnostics();
    }

    private void LoadDiagnostics()
    {
        // Memory Usage
        var process = Process.GetCurrentProcess();
        AppMemoryUsage = $"{process.WorkingSet64 / 1024 / 1024:N0} MB";

        // Cache Size
        var cachePath = Path.Combine(AppContext.BaseDirectory, "cache");
        if (Directory.Exists(cachePath))
        {
            var cacheSize = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            CacheSize = $"{cacheSize / 1024 / 1024:N2} MB";
        }

        // Database Size
        var dbPath = Path.Combine(AppContext.BaseDirectory, "yellowcake.db");
        if (File.Exists(dbPath))
        {
            DatabaseSize = $"{new FileInfo(dbPath).Length / 1024:N0} KB";
        }

        // Loaded Assemblies
        LoadedAssemblies.Clear();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
        {
            try
            {
                var name = assembly.GetName();
                LoadedAssemblies.Add(new AssemblyInfo
                {
                    Name = name.Name ?? "Unknown",
                    Version = name.Version?.ToString() ?? "Unknown",
                    Location = assembly.IsDynamic ? "Dynamic" : assembly.Location
                });
            }
            catch { }
        }

        // System Info
        SystemInfo = $"""
            OS: {RuntimeInformation.OSDescription}
            Architecture: {RuntimeInformation.OSArchitecture}
            Framework: {RuntimeInformation.FrameworkDescription}
            Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}
            Processors: {Environment.ProcessorCount}
            """;
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadDiagnostics();
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        try
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cache");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
                Directory.CreateDirectory(cachePath);
            }
            NotificationService.Instance.Success("Cache cleared successfully");
            LoadDiagnostics();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear cache");
            NotificationService.Instance.Error("Failed to clear cache");
        }
    }

    [RelayCommand]
    private async Task ExportDiagnostics()
    {
        try
        {
            var report = $"""
                Yellowcake Diagnostics Report
                Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                
                === SYSTEM INFO ===
                {SystemInfo}
                
                === MEMORY USAGE ===
                Working Set: {AppMemoryUsage}
                
                === STORAGE ===
                Cache: {CacheSize}
                Database: {DatabaseSize}
                
                === LOADED ASSEMBLIES ===
                {string.Join("\n", LoadedAssemblies.Select(a => $"{a.Name} {a.Version}"))}
                """;

            var path = Path.Combine(Path.GetTempPath(), $"yellowcake-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, report);
            
            NotificationService.Instance.Success($"Diagnostics exported to {path}");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export diagnostics");
            NotificationService.Instance.Error("Failed to export diagnostics");
        }
    }
}

public class AssemblyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public class PerformanceMetric
{
    public string Operation { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string FormattedDuration => $"{Duration.TotalMilliseconds:F2} ms";
}