using Serilog;
using System;
using System.IO;

namespace Yellowcake.Services;

public class PathService
{
    private readonly DatabaseService _db;
    private string? _gameDirectory;

    public PathService(DatabaseService db)
    {
        _db = db;
        LoadGamePath();
    }

    public string? GamePath
    {
        get => _gameDirectory;
        set => _gameDirectory = value;
    }

    public string? InstallRoot => _gameDirectory;

    public void SetGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _gameDirectory = File.Exists(path)
            ? Path.GetDirectoryName(path)
            : path;
    }

    public bool IsBepInExInstalled()
    {
        if (string.IsNullOrEmpty(_gameDirectory)) return false;

        var hasProxyDll = File.Exists(Path.Combine(_gameDirectory, "winhttp.dll"));
        var hasBepInFolder = Directory.Exists(Path.Combine(_gameDirectory, "BepInEx", "core"));

        return hasProxyDll && hasBepInFolder;
    }

    public void SaveGamePath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        try
        {
            _db.SaveSetting("GamePath", exePath);
            SetGamePath(exePath);
            Log.Information("Game path updated and saved: {Path}", exePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save game path setting.");
        }
    }

    public string? LoadGamePath()
    {
        try
        {
            var exePath = _db.GetSetting("GamePath");
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                SetGamePath(exePath);
                return exePath;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load game path from database.");
        }

        return null;
    }

    public string GetPluginsDirectory() =>
        Path.Combine(_gameDirectory ?? string.Empty, "BepInEx", "plugins");

    public string GetVoicePacksDirectory() =>
        Path.Combine(GetPluginsDirectory(), "WSOYappinator", "audio");
}