using System;
using System.IO;
using Avalonia;
using Serilog;

namespace Yellowcake
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called.
        [STAThread]
        public static void Main(string[] args)
        {
            // 1. Initialize Serilog
            string logPath = Path.Combine(AppContext.BaseDirectory, "logs", "yellowcake-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug() // Writes to Visual Studio's Output window
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            try
            {
                Log.Information("Yellowcake Mod Manager Starting...");

                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Capture startup failures (e.g., missing dependencies, DLL conflicts)
                Log.Fatal(ex, "The application failed to start correctly.");
                throw;
            }
            finally
            {
                // Ensure all logs are written to disk before closing
                Log.CloseAndFlush();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace(); // Redirects Avalonia internal logs to System.Diagnostics.Trace
    }
}