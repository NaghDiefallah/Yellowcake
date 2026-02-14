using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace Yellowcake.Helpers;

public static class JunctionManager
{
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || 
                                       RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                                       RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static void Create(string junctionPoint, string targetDir, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(junctionPoint) || string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("Junction and target paths cannot be empty.");

        var normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedJunction = Path.GetFullPath(junctionPoint).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(normalizedTarget))
            throw new DirectoryNotFoundException($"Target directory does not exist: {normalizedTarget}");

        PrepareJunctionPoint(normalizedJunction, overwrite);
        EnsureParentDirectoryExists(normalizedJunction);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CreateWindowsJunction(normalizedJunction, normalizedTarget);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            CreateUnixSymlink(normalizedJunction, normalizedTarget);
        }
        else
        {
            throw new PlatformNotSupportedException("Symbolic links are not supported on this platform.");
        }

        Log.Information("Created junction/symlink: {Junction} -> {Target}", normalizedJunction, normalizedTarget);
    }

    public static void Remove(string junctionPoint)
    {
        if (!Directory.Exists(junctionPoint) && !File.Exists(junctionPoint)) return;

        try
        {
            if (IsSymbolicLink(junctionPoint))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var di = new DirectoryInfo(junctionPoint);
                    di.Delete();
                }
                else
                {
                    File.Delete(junctionPoint);
                }
                
                Log.Information("Removed junction/symlink: {Path}", junctionPoint);
            }
            else
            {
                Directory.Delete(junctionPoint, true);
                Log.Information("Removed directory: {Path}", junctionPoint);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove junction/symlink: {Path}", junctionPoint);
            throw;
        }
    }

    public static bool IsSymbolicLink(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return di.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return fi.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error checking if path is symbolic link: {Path}", path);
        }

        return false;
    }

    private static void PrepareJunctionPoint(string path, bool overwrite)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;

        if (!overwrite)
            throw new InvalidOperationException($"The junction point already exists: {path}");

        Remove(path);
    }

    private static void EnsureParentDirectoryExists(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
            Log.Debug("Created parent directory: {Path}", parent);
        }
    }

    private static void CreateWindowsJunction(string junction, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) 
                throw new InvalidOperationException("Failed to start the junction creation process.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                if (string.IsNullOrWhiteSpace(error)) 
                    error = process.StandardOutput.ReadToEnd();
                
                throw new InvalidOperationException($"mklink failed with exit code {process.ExitCode}: {error.Trim()}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Unable to execute mklink. Administrator privileges may be required.", ex);
        }
    }

    private static void CreateUnixSymlink(string symlink, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/ln",
            Arguments = $"-s \"{target}\" \"{symlink}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) 
                throw new InvalidOperationException("Failed to start the symlink creation process.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                if (string.IsNullOrWhiteSpace(error)) 
                    error = process.StandardOutput.ReadToEnd();
                
                throw new InvalidOperationException($"ln failed with exit code {process.ExitCode}: {error.Trim()}");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Unable to execute ln command. Check permissions.", ex);
        }
    }

    public static string? GetTarget(string junctionPoint)
    {
        try
        {
            if (!IsSymbolicLink(junctionPoint)) return null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new DirectoryInfo(junctionPoint).LinkTarget;
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/readlink",
                    Arguments = $"\"{junctionPoint}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return process.ExitCode == 0 ? output : null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get junction target for: {Path}", junctionPoint);
            return null;
        }
    }
}