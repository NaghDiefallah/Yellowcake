using FluentAssertions;
using System.IO.Compression;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class InstallServiceTests
{
    [Fact]
    public void ExtractArchiveToPath_ShouldStripSingleSharedRootFolder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "mod.zip");
            var targetPath = Path.Combine(tempRoot, "out");

            using (var fs = File.Create(archivePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddZipEntry(zip, "MyMod/MyMod.dll", "binary");
                AddZipEntry(zip, "MyMod/config/settings.json", "{}");
            }

            var installService = CreateInstallService();
            installService.ExtractArchiveToPath(archivePath, targetPath, CancellationToken.None);

            File.Exists(Path.Combine(targetPath, "MyMod.dll")).Should().BeTrue();
            File.Exists(Path.Combine(targetPath, "config", "settings.json")).Should().BeTrue();
            Directory.Exists(Path.Combine(targetPath, "MyMod")).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExtractArchiveToPath_ShouldSkipPathTraversalEntries()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "unsafe.zip");
            var targetPath = Path.Combine(tempRoot, "out");
            var escapedPath = Path.Combine(tempRoot, "evil.txt");

            using (var fs = File.Create(archivePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddZipEntry(zip, "safe.txt", "ok");
                AddZipEntry(zip, "../evil.txt", "bad");
            }

            var installService = CreateInstallService();
            installService.ExtractArchiveToPath(archivePath, targetPath, CancellationToken.None);

            File.Exists(Path.Combine(targetPath, "safe.txt")).Should().BeTrue();
            File.Exists(escapedPath).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static InstallService CreateInstallService()
    {
        var db = new DatabaseService(":memory:");
        var pathService = new PathService(db);
        return new InstallService(pathService);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "YellowcakeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private static void AddZipEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
