using FluentAssertions;
using Octokit;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class ModDiscoveryTests : IDisposable
{
    private readonly List<string> _cleanupPaths = new();

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldDiscoverExternallyInstalledPlugin()
    {
        var env = CreateEnvironment();
        var modId = "external_plugin_" + Guid.NewGuid().ToString("N");

        var pluginDir = Path.Combine(env.GameDir, "BepInEx", "plugins", modId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.dll"), "x");

        var result = env.ModService.ReconcileInstalledModsWithDisk([CreateRemoteMod(modId)]);
        var stored = env.Db.GetAll<Mod>("addons").Single(m => m.Id == modId);

        result.Discovered.Should().Be(1);
        stored.IsInstalled.Should().BeTrue();
        stored.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldMarkDbModRemovedWhenMissingFromDisk()
    {
        var env = CreateEnvironment();
        var modId = "deleted_plugin_" + Guid.NewGuid().ToString("N");

        var local = CreateRemoteMod(modId);
        local.MarkAsInstalled("1.0.0");
        local.IsEnabled = true;
        env.Db.Upsert("addons", local);

        var result = env.ModService.ReconcileInstalledModsWithDisk([CreateRemoteMod(modId)]);
        var stored = env.Db.GetAll<Mod>("addons").Single(m => m.Id == modId);

        result.Removed.Should().Be(1);
        stored.IsInstalled.Should().BeFalse();
        stored.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldDiscoverLegacyVoicePackStorage()
    {
        var env = CreateEnvironment();
        var modId = "legacy_voice_" + Guid.NewGuid().ToString("N");

        var legacyVoicePath = Path.Combine(PathService.GetModsDirectory(), "WSOYappinator", "audio", modId);
        Directory.CreateDirectory(legacyVoicePath);
        File.WriteAllText(Path.Combine(legacyVoicePath, "line.ogg"), "x");
        _cleanupPaths.Add(legacyVoicePath);

        var voiceMod = CreateRemoteMod(modId);
        voiceMod.IsVoicePack = true;

        var result = env.ModService.ReconcileInstalledModsWithDisk([voiceMod]);
        var stored = env.Db.GetAll<Mod>("addons").Single(m => m.Id == modId);

        result.Discovered.Should().Be(1);
        stored.IsInstalled.Should().BeTrue();
        stored.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldUpdateExistingDbEntryWhenExternalStateChanges()
    {
        var env = CreateEnvironment();
        var modId = "toggle_plugin_" + Guid.NewGuid().ToString("N");

        var local = CreateRemoteMod(modId);
        local.MarkAsInstalled("1.0.0");
        local.IsEnabled = false;
        env.Db.Upsert("addons", local);

        var pluginDir = Path.Combine(env.GameDir, "BepInEx", "plugins", modId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.dll"), "x");

        var result = env.ModService.ReconcileInstalledModsWithDisk([CreateRemoteMod(modId)]);
        var stored = env.Db.GetAll<Mod>("addons").Single(m => m.Id == modId);

        result.Updated.Should().Be(1);
        stored.IsInstalled.Should().BeTrue();
        stored.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldDiscoverUnknownExternalPluginWithoutManifestEntry()
    {
        var env = CreateEnvironment();
        var modId = "external_only_" + Guid.NewGuid().ToString("N");

        var pluginDir = Path.Combine(env.GameDir, "BepInEx", "plugins", modId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.dll"), "x");

        var result = env.ModService.ReconcileInstalledModsWithDisk([]);
        var stored = env.Db.GetAll<Mod>("addons").Single(m => m.Id == modId);

        result.Discovered.Should().Be(1);
        result.ExternalDiscovered.Should().Be(1);
        stored.Source.Should().Be("External");
        stored.IsInstalled.Should().BeTrue();
        stored.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldNotDoubleCountWhenStorageAndGameTargetsExist()
    {
        var env = CreateEnvironment();
        var modId = "dual_location_" + Guid.NewGuid().ToString("N");

        var pluginDir = Path.Combine(env.GameDir, "BepInEx", "plugins", modId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.dll"), "x");

        var storageDir = Path.Combine(PathService.GetModsDirectory(), modId);
        Directory.CreateDirectory(storageDir);
        File.WriteAllText(Path.Combine(storageDir, "copy.dll"), "x");
        _cleanupPaths.Add(storageDir);

        var result = env.ModService.ReconcileInstalledModsWithDisk([]);
        var stored = env.Db.GetAll<Mod>("addons").Where(m => m.Id == modId).ToList();

        result.Discovered.Should().Be(1);
        stored.Should().HaveCount(1);
    }

    [Fact]
    public void ReconcileInstalledModsWithDisk_ShouldDoNothingWhenNoDiskOrDbMods()
    {
        var env = CreateEnvironment();

        var result = env.ModService.ReconcileInstalledModsWithDisk([]);

        result.TotalChanges.Should().Be(0);
        env.Db.GetAll<Mod>("addons").Should().BeEmpty();
    }

    private TestEnvironment CreateEnvironment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "YellowcakeTests", "migration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        _cleanupPaths.Add(tempRoot);

        var dbPath = Path.Combine(tempRoot, "test.db");
        var db = new DatabaseService(dbPath);
        var pathService = new PathService(db);

        var gameDir = Path.Combine(tempRoot, "game");
        Directory.CreateDirectory(gameDir);

        var gameExe = Path.Combine(gameDir, "NuclearOption.exe");
        File.WriteAllText(gameExe, "stub");

        pathService.SaveGamePath(gameExe);

        var installService = new InstallService(pathService);
        var http = new HttpClient();
        var gh = new GitHubClient(new ProductHeaderValue("Yellowcake-Test"));
        var modService = new ModService(db, installService, http, gh);

        return new TestEnvironment(db, modService, gameDir);
    }

    private static Mod CreateRemoteMod(string modId)
    {
        return new Mod
        {
            Id = modId,
            Name = modId,
            Artifacts =
            [
                new Yellowcake.Models.Artifact
                {
                    Category = "release",
                    Type = "Plugin",
                    Version = "1.0.0",
                    DownloadUrl = "https://example.test/mod.zip"
                }
            ]
        };
    }

    public void Dispose()
    {
        foreach (var path in _cleanupPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private sealed record TestEnvironment(DatabaseService Db, ModService ModService, string GameDir);
}
