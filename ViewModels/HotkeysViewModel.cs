using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Yellowcake.ViewModels;

public partial class HotkeysViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<HotkeyGroup> _hotkeyGroups = new();

    public HotkeysViewModel()
    {
        LoadHotkeys();
    }

    private void LoadHotkeys()
    {
        _hotkeyGroups.Clear();

        _hotkeyGroups.Add(new HotkeyGroup
        {
            Category = "Navigation",
            Hotkeys = new()
            {
                new Hotkey { Keys = "Ctrl + F", Description = "Focus search" },
                new Hotkey { Keys = "Ctrl + 1", Description = "Go to Library tab" },
                new Hotkey { Keys = "Ctrl + 2", Description = "Go to Browse tab" },
                new Hotkey { Keys = "Ctrl + ,", Description = "Open Settings" },
                new Hotkey { Keys = "Escape", Description = "Close overlays" }
            }
        });

        _hotkeyGroups.Add(new HotkeyGroup
        {
            Category = "Mod Management",
            Hotkeys = new()
            {
                new Hotkey { Keys = "Ctrl + D", Description = "Download selected mod" },
                new Hotkey { Keys = "Delete", Description = "Uninstall selected mod" },
                new Hotkey { Keys = "Ctrl + E", Description = "Toggle mod enabled state" },
                new Hotkey { Keys = "Ctrl + B", Description = "Toggle batch mode" },
                new Hotkey { Keys = "Ctrl + A", Description = "Select all (in batch mode)" },
                new Hotkey { Keys = "Ctrl + Shift + A", Description = "Deselect all (in batch mode)" }
            }
        });

        _hotkeyGroups.Add(new HotkeyGroup
        {
            Category = "Tools",
            Hotkeys = new()
            {
                new Hotkey { Keys = "F5", Description = "Refresh mod list" },
                new Hotkey { Keys = "Ctrl + Shift + D", Description = "Open Diagnostics" },
                new Hotkey { Keys = "Ctrl + L", Description = "Open Log Viewer" },
                new Hotkey { Keys = "Ctrl + P", Description = "Open Performance Dashboard" },
                new Hotkey { Keys = "F1 or ?", Description = "Show this help" }
            }
        });

        _hotkeyGroups.Add(new HotkeyGroup
        {
            Category = "Window",
            Hotkeys = new()
            {
                new Hotkey { Keys = "Ctrl + W", Description = "Close window" },
                new Hotkey { Keys = "F11", Description = "Toggle fullscreen" },
                new Hotkey { Keys = "Ctrl + +", Description = "Zoom in" },
                new Hotkey { Keys = "Ctrl + -", Description = "Zoom out" },
                new Hotkey { Keys = "Ctrl + 0", Description = "Reset zoom" }
            }
        });
    }
}

public class HotkeyGroup
{
    public string Category { get; set; } = string.Empty;
    public ObservableCollection<Hotkey> Hotkeys { get; set; } = new();
}

public class Hotkey
{
    public string Keys { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}