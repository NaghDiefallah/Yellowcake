using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class LazyModViewModel : ObservableObject
{
    private readonly Mod _mod;
    private readonly ModService _modService;
    private ModDetails? _details;
    private bool _isLoading;
    private bool _isLoaded;

    [ObservableProperty] private string _fullDescription = string.Empty;
    [ObservableProperty] private string _changelogText = string.Empty;
    [ObservableProperty] private int _downloadCount;
    [ObservableProperty] private double _rating;
    [ObservableProperty] private bool _hasDetails;

    public Mod Mod => _mod;

    public LazyModViewModel(Mod mod, ModService modService)
    {
        _mod = mod ?? throw new ArgumentNullException(nameof(mod));
        _modService = modService ?? throw new ArgumentNullException(nameof(modService));
    }

    public async Task<ModDetails?> GetDetailsAsync()
    {
        if (_isLoaded) return _details;
        if (_isLoading) return null; // Already loading

        _isLoading = true;
        
        try
        {
            _details = await LoadDetailsAsync();
            
            if (_details != null)
            {
                FullDescription = _details.FullDescription ?? _mod.Description;
                ChangelogText = _details.Changelog ?? "No changelog available";
                DownloadCount = _details.DownloadCount;
                Rating = _details.Rating;
                HasDetails = true;
                _isLoaded = true;
            }

            return _details;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load details for mod {ModId}", _mod.Id);
            return null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task<ModDetails?> LoadDetailsAsync()
    {
        try
        {
            // Simulate loading details from API/manifest
            await Task.Delay(100); // Small delay to avoid hammering the service

            // Get the changelog from the mod if available
            var changelog = _mod.Changelog ?? "• Initial release";

            var details = new ModDetails
            {
                ModId = _mod.Id,
                FullDescription = _mod.Description ?? string.Empty,
                Changelog = changelog,
                DownloadCount = 0,
                Rating = 0.0,
                LastUpdated = DateTime.Now, // Use current date as fallback
                Tags = _mod.Tags?.ToArray() ?? Array.Empty<string>(),
                Screenshots = _mod.ScreenshotUrls?.ToArray() ?? Array.Empty<string>()
            };

            return details;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load mod details for {ModId}", _mod.Id);
            return null;
        }
    }

    public void InvalidateCache()
    {
        _isLoaded = false;
        _details = null;
        HasDetails = false;
    }
}

public class ModDetails
{
    public string ModId { get; set; } = string.Empty;
    public string FullDescription { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public double Rating { get; set; }
    public DateTime LastUpdated { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Screenshots { get; set; } = Array.Empty<string>();
}