using Avalonia;
using Avalonia.Media;
using Serilog;
using System;
using System.IO;

namespace Yellowcake;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDirectory)) Directory.CreateDirectory(logDirectory);

        string logPath = Path.Combine(logDirectory, "yellowcake-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Yellowcake Mod Manager Initializing...");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new Win32PlatformOptions
            {
                OverlayPopups = true,
                RenderingMode = new[] { Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software }
            });
    }
}