using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Yellowcake.ViewModels;

public partial class ScreenshotViewerViewModel : ObservableObject
{
    private readonly HttpClient _http = new();
    private readonly Window? _window;

    [ObservableProperty] private ObservableCollection<Bitmap> _screenshots = new();
    [ObservableProperty] private Bitmap? _currentScreenshot;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _title = string.Empty;

    public bool CanGoPrevious => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < Screenshots.Count - 1;

    // New parameterless constructor for default usage
    public ScreenshotViewerViewModel()
    {
    }

    // Existing constructor now with optional window and params
    public ScreenshotViewerViewModel(List<string> screenshots, int startIndex = 0, Window? window = null)
    {
        _window = window;

        if (screenshots != null && screenshots.Any())
        {
            Title = "Mod - Screenshots";
            _ = LoadScreenshotsAsync(screenshots.ToArray(), "Mod");
            CurrentIndex = startIndex >= 0 && startIndex < screenshots.Count ? startIndex : 0;
        }
    }

    public async Task LoadScreenshotsAsync(string[] imageUrls, string modName)
    {
        try
        {
            IsLoading = true;
            Title = $"{modName} - Screenshots";
            Screenshots.Clear();

            foreach (var url in imageUrls)
            {
                try
                {
                    var bitmap = await LoadImageAsync(url);
                    if (bitmap != null)
                    {
                        Screenshots.Add(bitmap);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load screenshot: {Url}", url);
                }
            }

            if (Screenshots.Any())
            {
                CurrentIndex = 0;
                CurrentScreenshot = Screenshots[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<Bitmap?> LoadImageAsync(string url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load image from {Url}", url);
            return null;
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (CanGoPrevious)
        {
            CurrentIndex--;
            CurrentScreenshot = Screenshots[CurrentIndex];
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CanGoNext)
        {
            CurrentIndex++;
            CurrentScreenshot = Screenshots[CurrentIndex];
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    [RelayCommand]
    private void Close() => _window?.Close();

    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}