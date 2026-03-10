using FluentAssertions;
using Yellowcake.Models;

namespace Yellowcake.Tests;

public class ModModelTests
{
    [Fact]
    public void CleanVersion_ShouldNormalizeCommonFormats()
    {
        Mod.CleanVersion("v1.2.3").Should().Be("1.2.3");
        Mod.CleanVersion("V2.0.1-beta").Should().Be("2.0.1");
        Mod.CleanVersion("invalid").Should().Be("0.0.0");
        Mod.CleanVersion(null).Should().Be("0.0.0");
    }

    [Fact]
    public void Validate_ShouldFailWhenCoreFieldsMissing()
    {
        var mod = new Mod();

        var result = mod.Validate();

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_ShouldPassWhenReleaseArtifactHasDownloadUrl()
    {
        var mod = new Mod
        {
            Id = "mod.id",
            Name = "Mod Name",
            Artifacts =
            [
                new Artifact
                {
                    Category = "release",
                    Version = "1.0.0",
                    DownloadUrl = "https://example.test/mod.zip"
                }
            ]
        };

        var result = mod.Validate();

        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void GetReleaseArtifacts_ShouldReturnSortedDescendingByVersion()
    {
        var mod = new Mod
        {
            Artifacts =
            [
                new Artifact { Category = "release", Version = "1.0.0" },
                new Artifact { Category = "release", Version = "1.10.0" },
                new Artifact { Category = "beta", Version = "2.0.0" },
                new Artifact { Category = "release", Version = "1.2.0" }
            ]
        };

        var releases = mod.GetReleaseArtifacts();

        releases.Select(a => a.Version).Should().ContainInOrder("1.10.0", "1.2.0", "1.0.0");
    }

    [Fact]
    public void FinalizeFromManifest_ShouldDetectMissionAndVoiceAndLiveryFromMetadata()
    {
        var mission = new Mod
        {
            Name = "Island Mission Pack",
            Tags = ["mission"],
            Artifacts = [new Artifact { Type = "Mission", Category = "release", Version = "1.0.0", DownloadUrl = "https://example.test/a" }]
        };
        mission.FinalizeFromManifest();
        mission.IsMission.Should().BeTrue();

        var voice = new Mod
        {
            Name = "Pilot Voices",
            Tags = ["voice"],
            Artifacts = [new Artifact { Type = "Plugin", Category = "release", Version = "1.0.0", DownloadUrl = "https://example.test/b" }]
        };
        voice.FinalizeFromManifest();
        voice.IsVoicePack.Should().BeTrue();

        var livery = new Mod
        {
            Name = "F16 Skin Pack",
            Tags = ["livery"],
            Artifacts = [new Artifact { Type = "Livery", Category = "release", Version = "1.0.0", DownloadUrl = "https://example.test/c" }]
        };
        livery.FinalizeFromManifest();
        livery.IsLivery.Should().BeTrue();
    }

    [Fact]
    public void MarkAsInstalledAndUninstalled_ShouldMutateStateConsistently()
    {
        var mod = new Mod
        {
            Artifacts = [new Artifact { Category = "release", Version = "1.0.0", DownloadUrl = "https://example.test/mod.zip" }]
        };

        mod.MarkAsInstalled("1.0.0", "sha256:abc");

        mod.IsInstalled.Should().BeTrue();
        mod.InstalledVersionString.Should().Be("1.0.0");
        mod.InstalledArtifactHash.Should().Be("sha256:abc");

        mod.MarkAsUninstalled();

        mod.IsInstalled.Should().BeFalse();
        mod.IsEnabled.Should().BeFalse();
        mod.HasUpdate.Should().BeFalse();
        mod.InstalledVersionString.Should().BeNull();
        mod.InstalledArtifactHash.Should().BeNull();
    }
}
