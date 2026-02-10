using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Serilog;
using System;
using System.IO;
using System.Linq;
using Yellowcake.ViewModels;
using Yellowcake.Services;

namespace Yellowcake.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        AddHandler(DragDrop.DropEvent, Drop);
        AddHandler(DragDrop.DragOverEvent, DragOver);

        // Subscribe to search focus event
        DataContextChanged += OnDataContextChanged;
        
        // CRITICAL FIX: Initialize NotificationService when window is loaded
        Opened += MainWindow_Opened;
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        // Initialize the notification service with this window
        NotificationService.Instance.Initialize(this);
        Log.Information("NotificationService initialized with MainWindow");
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SearchFocusRequested -= OnSearchFocusRequested;
            vm.SearchFocusRequested += OnSearchFocusRequested;
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        // Find and focus the search TextBox
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        searchBox?.Focus();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.HandleKeyDown(e);
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        // Only allow files
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;

        try
        {
            var files = e.Data.GetFiles()?.ToArray();
            if (files == null || files.Length == 0) return;

            if (DataContext is not MainViewModel vm) return;

            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                var extension = Path.GetExtension(path).ToLowerInvariant();

                switch (extension)
                {
                    case ".dll":
                        await vm.InstallDirectDllCommand.ExecuteAsync(path);
                        break;

                    case ".zip":
                        await vm.InstallFromZipCommand.ExecuteAsync(path);
                        break;

                    case ".json" when Path.GetFileName(path).Contains("manifest", StringComparison.OrdinalIgnoreCase):
                        await vm.ImportManifestCommand.ExecuteAsync(path);
                        break;

                    default:
                        Log.Warning("Unsupported file type: {Extension}", extension);
                        NotificationService.Instance.Warning($"Unsupported file type: {extension}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling drag & drop");
            NotificationService.Instance.Error("Failed to process dropped files");
        }
    }
}