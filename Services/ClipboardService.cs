using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public static class ClipboardService
{
    private static IClipboard? GetClipboard()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
               ?.MainWindow?.Clipboard;
    }

    public static async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public static async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        return clipboard != null ? await ClipboardExtensions.TryGetTextAsync(clipboard) : null;
    }
}