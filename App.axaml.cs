using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yellowcake.Services;
using Yellowcake.ViewModels;
using Yellowcake.Views;
using System;
using System.IO;
using System.Linq;

namespace Yellowcake;

public partial class App : Application
{
    private readonly ThemeService _themeService = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var databaseService = new DatabaseService();

            ThemeService.Database = databaseService;
            _themeService.Initialize();

            var viewModel = new MainViewModel();

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;

            // CRITICAL: Initialize NotificationService AFTER window is created
            mainWindow.Opened += (s, e) =>
            {
                NotificationService.Instance.Initialize(mainWindow);
                Serilog.Log.Information("NotificationService initialized with MainWindow");
            };

            HandleCommandLineArguments(desktop.Args, viewModel);

            desktop.MainWindow.Closing += (s, e) =>
            {
                if (desktop.MainWindow.DataContext is MainViewModel vm && vm.MinimizeToTray)
                {
                    e.Cancel = true;
                    desktop.MainWindow.Hide();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleCommandLineArguments(string[]? args, MainViewModel vm)
    {
        if (args == null || args.Length == 0) return;

        if (args.Contains("--launch"))
        {
            if (vm.LaunchGameCommand.CanExecute(null))
            {
                vm.LaunchGameCommand.Execute(null);
            }
        }
    }

    public void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
        }
    }

    public void Shutdown()
    {
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void OnTrayShowClick(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayExitClick(object? sender, EventArgs e) => Shutdown();
}