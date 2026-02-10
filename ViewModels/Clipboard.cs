using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task CopyModId(Mod? mod)
    {
        if (mod == null) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(mod.Id);
                    NotificationService.Instance.Success($"Copied: {mod.Id}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy mod ID");
        }
    }

    [RelayCommand]
    private async Task CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                    NotificationService.Instance.Success("Copied to clipboard");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy text");
        }
    }
}