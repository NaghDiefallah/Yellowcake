using FluentAssertions;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class ModServiceTests
{
    [Fact]
    public void HasUpdate_ShouldReturnTrueWhenRemoteVersionIsNewer()
    {
        var installed = CreateMod("1.1.0");
        var remote = CreateMod("1.2.0");

        ModService.HasUpdate(installed, remote).Should().BeTrue();
    }

    [Fact]
    public void HasUpdate_ShouldReturnFalseWhenSameVersion()
    {
        var installed = CreateMod("1.2.0");
        var remote = CreateMod("1.2.0");

        ModService.HasUpdate(installed, remote).Should().BeFalse();
    }

    [Fact]
    public void HasUpdate_ShouldTreatNonStandardVersionsAsEqualWhenBothNormalizeToZero()
    {
        var installed = CreateMod("dev-build");
        var remote = CreateMod("stable-build");

        ModService.HasUpdate(installed, remote).Should().BeFalse();
    }

    [Fact]
    public void HasUpdate_ShouldReturnFalseWhenEitherModIsNull()
    {
        var mod = CreateMod("1.0.0");

        ModService.HasUpdate(mod, null!).Should().BeFalse();
        ModService.HasUpdate(null!, mod).Should().BeFalse();
    }

    private static Mod CreateMod(string version)
    {
        return new Mod
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "TestMod",
            Artifacts =
            [
                new Artifact
                {
                    Category = "release",
                    Version = version,
                    DownloadUrl = "https://example.test/mod.zip"
                }
            ]
        };
    }
}
