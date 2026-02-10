using Serilog;
using System;
using System.IO;
using System.Linq;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public static class InstallDebugHelper
{
    public static void LogInstallAttempt(Mod mod, DatabaseService db, PathService pathService)
    {
        Log.Information("=== INSTALL DEBUG START ===");
        Log.Information("Mod ID: {Id}", mod.Id);
        Log.Information("Mod Name: {Name}", mod.Name);
        Log.Information("Mod Version: {Version}", mod.Version);
        Log.Information("Download URL: {Url}", mod.DownloadUrl);
        Log.Information("Is Valid: {Valid}", mod.Validate().IsValid);
        
        var gamePath = db.GetSetting("GamePath");
        Log.Information("Game Path: {Path}", gamePath);
        Log.Information("Game Path Exists: {Exists}", !string.IsNullOrEmpty(gamePath) && File.Exists(gamePath));
        
        if (!string.IsNullOrEmpty(gamePath))
        {
            var gameDir = Path.GetDirectoryName(gamePath);
            Log.Information("Game Directory: {Dir}", gameDir);
            
            var pluginsDir = Path.Combine(gameDir ?? "", "BepInEx", "plugins");
            Log.Information("Plugins Directory: {Dir}", pluginsDir);
            Log.Information("Plugins Directory Exists: {Exists}", Directory.Exists(pluginsDir));
            
            if (Directory.Exists(pluginsDir))
            {
                var files = Directory.GetFiles(pluginsDir, "*.*", SearchOption.AllDirectories);
                Log.Information("Files in plugins: {Count}", files.Length);
            }
        }
        
        Log.Information("=== INSTALL DEBUG END ===");
    }

    public static void LogUninstallAttempt(Mod mod, DatabaseService db)
    {
        Log.Information("=== UNINSTALL DEBUG START ===");
        Log.Information("Mod ID: {Id}", mod.Id);
        Log.Information("Mod Name: {Name}", mod.Name);
        Log.Information("Is Installed: {Installed}", mod.IsInstalled);
        
        var dbMod = db.GetAll<Mod>("addons").FirstOrDefault(m => m.Id == mod.Id);
        Log.Information("Found in DB: {Found}", dbMod != null);
        
        Log.Information("=== UNINSTALL DEBUG END ===");
    }
}