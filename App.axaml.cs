using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Yellowcake.Services;
using Yellowcake.ViewModels;
using Yellowcake.Views;

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

        var tokens = new Queue<string>(args);

        while (tokens.Count > 0)
        {
            var flag = tokens.Dequeue().ToLowerInvariant();

            switch (flag)
            {
                case "--launch":
                    TryExecute(vm.LaunchGameCommand);
                    break;

                //case "--file":
                //    if (TryGetNext(tokens, out var filePath))
                //        TryExecute(vm.OpenFileCommand, filePath);
                //    break;

                //case "--user":
                //    if (TryGetNext(tokens, out var user))
                //        vm.CurrentUser = user;
                //    break;

                //case "--debug":
                //    vm.EnableLogging = true;
                //    break;

                default:
                    if (flag == "-l") goto case "--launch";
                    // if (flag == "-d") goto case "--debug";
                    Debug.WriteLine($"Unknown argument: {flag}");
                    break;
            }
        }
    }

    private bool TryGetNext(Queue<string> tokens, out string value)
    {
        value = string.Empty;
        if (tokens.TryPeek(out var next) && !next.StartsWith("-"))
        {
            value = tokens.Dequeue();
            return true;
        }
        return false;
    }

    private void TryExecute(ICommand command, object? parameter = null)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
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