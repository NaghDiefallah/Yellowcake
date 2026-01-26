using System;
using System.Diagnostics;
using System.IO;

namespace Yellowcake.Helpers;

public static class JunctionManager
{
    public static void Create(string junctionPoint, string targetDir, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(junctionPoint) || string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("Junction and target paths cannot be empty.");

        string normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedJunction = Path.GetFullPath(junctionPoint).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(normalizedTarget))
            throw new DirectoryNotFoundException($"Target directory does not exist: {normalizedTarget}");

        PrepareJunctionPoint(normalizedJunction, overwrite);

        EnsureParentDirectoryExists(normalizedJunction);

        ExecuteMkLink(normalizedJunction, normalizedTarget);
    }

    public static void Remove(string junctionPoint)
    {
        if (!Directory.Exists(junctionPoint)) return;

        var di = new DirectoryInfo(junctionPoint);
        if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            di.Delete();
        }
        else
        {
            Directory.Delete(junctionPoint, true);
        }
    }

    private static void PrepareJunctionPoint(string path, bool overwrite)
    {
        if (!Directory.Exists(path)) return;

        if (!overwrite)
            throw new InvalidOperationException($"The junction point already exists: {path}");

        Remove(path);
    }

    private static void EnsureParentDirectoryExists(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent != null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void ExecuteMkLink(string junction, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /j \"{junction}\" \"{target}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Failed to start the junction creation process.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                if (string.IsNullOrWhiteSpace(error)) error = process.StandardOutput.ReadToEnd();
                throw new Exception($"mklink failed with exit code {process.ExitCode}: {error.Trim()}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new Exception("System error: Unable to execute mklink. Check your permissions.", ex);
        }
    }
}