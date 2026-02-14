using Avalonia;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Yellowcake.Services;

namespace Yellowcake;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        File.WriteAllText("startup.log", $"App started at {DateTime.Now}\n");
        try
        {
            ConfigureLogging();
            
            Log.Information("Starting Avalonia application...");
            Log.Debug("Platform: {Platform}", Environment.OSVersion);
            
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            
            // Cross-platform error handling
            HandleFatalError(ex);
            
            Environment.Exit(1);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logPath, "yellowcake-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.Sink(InMemorySink.Instance)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Starting ===");
        Log.Information("Application Version: {Version}", 
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
        Log.Information("OS: {OS}", Environment.OSVersion);
        Log.Information(".NET Version: {Version}", Environment.Version);
        Log.Information("Platform: {Platform}", GetPlatformName());
        Log.Information("Base Directory: {Directory}", AppContext.BaseDirectory);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        try
        {
            Log.Debug("Configuring Avalonia AppBuilder...");
            
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Warning);
            
            Log.Debug("Avalonia AppBuilder configured successfully");
            
            return builder;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to configure Avalonia AppBuilder");
            throw;
        }
    }

    private static void HandleFatalError(Exception ex)
    {
        var errorMessage = $"""
            ================================================================================
            FATAL ERROR - Yellowcake Mod Manager
            ================================================================================
            
            The application encountered a fatal error and must close.
            
            Error: {ex.Message}
            
            Stack Trace:
            {ex.StackTrace}
            
            Log Location: {Path.Combine(AppContext.BaseDirectory, "logs")}
            
            Please check the log files for more details.
            ================================================================================
            """;
        // Write to console (cross-platform)
        Console.Error.WriteLine(errorMessage);
        
        // Write to a crash report file
        try
        {
            var crashReportPath = Path.Combine(AppContext.BaseDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(crashReportPath, $"{errorMessage}\n\nFull Exception:\n{ex}");
            Console.Error.WriteLine($"\nCrash report saved to: {crashReportPath}");
        }
        catch
        {
            // Ignore errors writing crash report
        }
    }

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"Windows {Environment.OSVersion.Version}";
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"Linux {Environment.OSVersion.Version}";
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"macOS {Environment.OSVersion.Version}";
        
        return $"Unknown ({RuntimeInformation.OSDescription})";
    }
}