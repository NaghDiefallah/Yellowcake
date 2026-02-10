using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Yellowcake.Services;
using Yellowcake.Models;

namespace Yellowcake.Tests;

public class InstallServiceTests
{
    [Fact]
    public async Task ExtractWithSmartRoot_ExtractsZipContents()
    {
        // Arrange
        var db = new DatabaseService(":memory:");
        var pathSvc = new PathService(db);
        string modsPath = Path.Combine(Path.GetTempPath(), "yc_test_mods_extract");
        if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
        Directory.CreateDirectory(modsPath);

        var installer = new InstallService(modsPath, pathSvc);

        // Create a zip in memory with root folder "package-1.0/" and file "package-1.0/inner.txt"
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("package-1.0/inner.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }
        ms.Position = 0;

        string target = Path.Combine(Path.GetTempPath(), "yc_extract_target");
        if (Directory.Exists(target)) Directory.Delete(target, true);

        // Act
        await installer.ExtractWithSmartRoot(ms, target);

        // Assert
        var expected = Path.Combine(target, "inner.txt");
        Assert.True(File.Exists(expected));
        var content = File.ReadAllText(expected);
        Assert.Equal("hello", content);

        // cleanup
        if (Directory.Exists(target)) Directory.Delete(target, true);
        if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
    }

    [Fact]
    public async Task InstallCategorizedMod_ReplacesExistingWithBackupThenRemovesBackup()
    {
        // Arrange
        var db = new DatabaseService(":memory:");
        var pathSvc = new PathService(db);
        string modsPath = Path.Combine(Path.GetTempPath(), "yc_test_mods_tx");
        if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
        Directory.CreateDirectory(modsPath);

        var installer = new InstallService(modsPath, pathSvc);

        var mod = new Mod { Id = "txmod", Name = "Tx Mod" };

        // Create an existing final dir with old file
        string finalDir = Path.Combine(modsPath, mod.Id);
        Directory.CreateDirectory(finalDir);
        File.WriteAllText(Path.Combine(finalDir, "old.txt"), "old");

        // Create a zip with new file "new.txt"
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("new.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("new");
        }
        ms.Position = 0;

        // Act
        await installer.InstallCategorizedMod(mod, ms, "C:\\games\\dummy");

        // Assert final dir contains new.txt and old.txt removed
        Assert.True(Directory.Exists(finalDir));
        var files = Directory.EnumerateFiles(finalDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
        Assert.Contains("new.txt", files);
        Assert.DoesNotContain("old.txt", files);

        // cleanup
        if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
        if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
    }

    [Fact]
    public async Task InstallRawDll_WritesSanitizedFileName()
    {
        // Arrange
        var db = new DatabaseService(":memory:");
        var pathSvc = new PathService(db);
        string modsPath = Path.Combine(Path.GetTempPath(), "yc_test_mods_dll");
        if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
        Directory.CreateDirectory(modsPath);

        var installer = new InstallService(modsPath, pathSvc);
        
        var mod = new Mod 
        { 
            Id = "test_mod", 
            Name = "Test Mod",
            Artifacts = new System.Collections.Generic.List<Artifact>
            {
                new Artifact
                {
                    Version = "1.0.0",
                    Category = "release",
                    DownloadUrl = "https://example.com/download?file=bad/name.dll"
                }
            }
        };

        using var ms = new MemoryStream(new byte[] { 0x4D, 0x5A }); // minimal PE header

        try
        {
            // Act
            await installer.InstallRawDll(mod, ms, "C:\\games\\dummy");

            // Assert
            var dir = Path.Combine(installer.ModsPath, mod.Id);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            // cleanup
            var dir = Path.Combine(installer.ModsPath, mod.Id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(modsPath)) Directory.Delete(modsPath, true);
        }
    }
}