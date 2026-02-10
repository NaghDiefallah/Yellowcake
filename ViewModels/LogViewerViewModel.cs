using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class LogViewerViewModel : ObservableObject
{
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedLevel = "All";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private ObservableCollection<LogEntry> _filteredLogs = new();
    [ObservableProperty] private bool _isLogViewerWindowOpen;

    private ObservableCollection<LogEntry> _allLogs = new();
    private DispatcherTimer? _refreshTimer;
    private int _lastLogCount = 0;

    public string[] LogLevels { get; } = new[] { "All", "Debug", "Information", "Warning", "Error", "Fatal" };
    
    public int TotalLogCount => _allLogs.Count;
    public int FilteredLogCount => FilteredLogs.Count;

    public LogViewerViewModel()
    {
        LoadLogs();
        StartAutoRefresh();

    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    private void StartAutoRefresh()
    {
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1) // Check every second for better responsiveness
        };
        _refreshTimer.Tick += (s, e) => LoadLogs();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private void Refresh()
    {
        _lastLogCount = 0; // Force full refresh
        LoadLogs();
        StatusText = $"Refreshed at {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void Clear()
    {
        InMemorySink.Instance.Clear();
        _allLogs.Clear();
        FilteredLogs.Clear();
        _lastLogCount = 0;
        OnPropertyChanged(nameof(TotalLogCount));
        OnPropertyChanged(nameof(FilteredLogCount));
        StatusText = "Logs cleared";
        Log.Information("Log viewer cleared");
    }

    [RelayCommand]
    private async Task Export()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportPath = Path.Combine(Path.GetTempPath(), $"yellowcake_logs_{timestamp}.txt");

            var lines = FilteredLogs.Select(l => 
            {
                var message = $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{l.Level}] {l.Message}";
                return message;
            });

            await File.WriteAllLinesAsync(exportPath, lines);

            NotificationService.Instance.Success($"Logs exported to {exportPath}");
            StatusText = $"Exported {FilteredLogs.Count} logs to {Path.GetFileName(exportPath)}";
            Log.Information("Logs exported to {Path}", exportPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export logs");
            NotificationService.Instance.Error("Failed to export logs");
        }
    }

    private void LoadLogs()
    {
        try
        {
            var logEvents = InMemorySink.Instance.GetLogs();
            
            // Only update if there are new logs
            if (logEvents.Length == _lastLogCount)
                return;

            _lastLogCount = logEvents.Length;
            _allLogs.Clear();
            
            foreach (var logEvent in logEvents)
            {
                var entry = new LogEntry
                {
                    Timestamp = logEvent.Timestamp,
                    Level = logEvent.Level,
                    Message = logEvent.Message,
                    LevelBrush = GetBrushForLevel(logEvent.Level)
                };

                _allLogs.Add(entry);
            }

            ApplyFilter();
            OnPropertyChanged(nameof(TotalLogCount));
        }
        catch (Exception ex)
        {
            // Don't log here to avoid recursion
            System.Diagnostics.Debug.WriteLine($"Failed to load logs: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allLogs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(l => 
                l.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                l.Level.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedLevel != "All")
        {
            filtered = filtered.Where(l => 
                l.Level.Equals(SelectedLevel, StringComparison.OrdinalIgnoreCase));
        }

        // Get the most recent logs at the top
        var orderedLogs = filtered.OrderBy(l => l.Timestamp).ToList();

        FilteredLogs.Clear();
        foreach (var log in orderedLogs)
        {
            FilteredLogs.Add(log);
        }
        
        OnPropertyChanged(nameof(FilteredLogCount));
    }

    private static SolidColorBrush GetBrushForLevel(string level)
    {
        var normalizedLevel = level.Trim().ToUpperInvariant();
        
        var color = normalizedLevel switch
        {
            "DEBUG" or "VERBOSE" or "DBG" => Color.Parse("#6C757D"),
            "INFORMATION" or "INFO" or "INF" => Color.Parse("#0D6EFD"),
            "WARNING" or "WARN" or "WRN" => Color.Parse("#FFC107"),
            "ERROR" or "ERR" => Color.Parse("#DC3545"),
            "FATAL" or "CRITICAL" or "FTL" or "CRT" => Color.Parse("#8B0000"),
            _ => Color.Parse("#6C757D")
        };
        
        return new SolidColorBrush(color);
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public SolidColorBrush LevelBrush { get; set; } = new(Colors.Gray);
}