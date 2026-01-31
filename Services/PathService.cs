using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Yellowcake.Services;

public class PathService
{
    private readonly DatabaseService _db;
    private string? _gameDirectory;
    private string? _gameExePath;

    public PathService(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        LoadGamePath();
    }

    public string? GamePath => _gameExePath;
    public string? InstallRoot => _gameDirectory;

    public void SetGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NuclearOption.exe" : "NuclearOption";

        if (File.Exists(path))
        {
            _gameExePath = path;
            _gameDirectory = Path.GetDirectoryName(path);
        }
        else if (Directory.Exists(path))
        {
            _gameDirectory = path;
            _gameExePath = Path.Combine(path, exeName);
        }

        if (!string.IsNullOrEmpty(_gameDirectory))
        {
            EnsureDirectories();
        }
    }

    public bool IsBepInExInstalled()
    {
        if (string.IsNullOrEmpty(_gameDirectory)) return false;

        string loaderFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "winhttp.dll" : "libdoorstop.so";

        return (File.Exists(Path.Combine(_gameDirectory, loaderFile)) || File.Exists(Path.Combine(_gameDirectory, "version.dll"))) &&
               Directory.Exists(Path.Combine(_gameDirectory, "BepInEx", "core"));
    }

    public void SaveGamePath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        try
        {
            _db.SaveSetting("GamePath", exePath);
            SetGamePath(exePath);
            Log.Information("Platform-specific game path saved: {Path}", exePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist game path.");
        }
    }

    public string? LoadGamePath()
    {
        try
        {
            var exePath = _db.GetSetting("GamePath");
            if (!string.IsNullOrEmpty(exePath))
            {
                SetGamePath(exePath);
                return exePath;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve game path.");
        }

        return null;
    }

    public string GetPluginsDirectory() => GetOrCreateDirectory(Path.Combine(_gameDirectory ?? string.Empty, "BepInEx", "plugins"));

    public string GetVoicePacksDirectory() => GetOrCreateDirectory(Path.Combine(GetPluginsDirectory(), "WSOYappinator", "audio"));

    public string GetMissionsDirectory()
    {
        string missionPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = Path.Combine(Path.GetDirectoryName(localAppData) ?? string.Empty, "LocalLow");
            missionPath = Path.Combine(localLow, "Shockfront", "NuclearOption", "Missions");
        }
        else
        {
            missionPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".steam", "steam", "steamapps", "compatdata", "2168680", "pfx", "drive_c",
                "users", "steamuser", "AppData", "LocalLow", "Shockfront", "NuclearOption", "Missions");
        }

        return GetOrCreateDirectory(missionPath);
    }

    private string GetOrCreateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Log.Debug("Validated directory structure: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Could not create or access directory {Path}: {Message}", path, ex.Message);
        }
        return path;
    }

    private void EnsureDirectories()
    {
        try
        {
            GetPluginsDirectory();
            GetVoicePacksDirectory();
            GetMissionsDirectory();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Critical failure during directory synchronization.");
        }
    }
}