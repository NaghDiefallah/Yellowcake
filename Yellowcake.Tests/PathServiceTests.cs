using FluentAssertions;
using Yellowcake.Services;

namespace Yellowcake.Tests;

public class PathServiceTests
{
    [Fact]
    public void GetModsDirectory_ShouldCreateAndReturnStableDirectory()
    {
        var modsDir = PathService.GetModsDirectory();

        modsDir.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(modsDir).Should().BeTrue();
        Path.GetFileName(modsDir).Should().Be("mods");
    }
}
