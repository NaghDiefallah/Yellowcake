using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class MissionService
{
    private readonly PathService _pathService;

    public MissionService(PathService pathService) => _pathService = pathService;

    public List<string> GetInstalledMissionFolders()
    {
        try
        {
            string directory = _pathService.GetMissionsDirectory();
            if (!Directory.Exists(directory)) return [];

            return Directory.EnumerateDirectories(directory)
                .Where(IsMissionFolder)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve installed mission folders.");
            return [];
        }
    }

    public void UninstallMission(Mod mod)
    {
        try
        {
            string directory = _pathService.GetMissionsDirectory();
            if (!Directory.Exists(directory)) return;

            string modFolder = Path.Combine(directory, mod.Id);
            if (Directory.Exists(modFolder))
            {
                Directory.Delete(modFolder, true);
                Log.Information("Successfully removed mission folder: {Folder}", mod.Id);
                return;
            }

            var folders = Directory.GetDirectories(directory);
            foreach (var folder in folders)
            {
                string folderName = Path.GetFileName(folder);
                if (folderName.Contains(mod.Id, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(mod.Name) && folderName.Contains(mod.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    Directory.Delete(folder, true);
                    Log.Information("Removed matching mission folder: {Folder}", folderName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to uninstall mission: {Id}", mod.Id);
        }
    }

    public bool IsMissionFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return false;

        try
        {
            var files = Directory.GetFiles(folderPath, "*.json");
            return files.Length >= 2;
        }
        catch
        {
            return false;
        }
    }

    public void OpenMissionsFolder()
    {
        try
        {
            string path = _pathService.GetMissionsDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open missions folder.");
        }
    }
}