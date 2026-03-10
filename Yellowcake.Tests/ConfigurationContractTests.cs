using FluentAssertions;
using Octokit;
using Yellowcake.Services;
using Yellowcake.ViewModels;

namespace Yellowcake.Tests;

public class ConfigurationContractTests
{
    [Fact]
    public void OfficialEndpoints_ShouldMatchExpectedUrls()
    {
        MainViewModel.OfficialManifestUrl.Should().Be("https://kopterbuzz.github.io/NOMNOM/manifest/manifest.json");
        MainViewModel.OfficialVersionUrl.Should().Be("https://kopterbuzz.github.io/NOMNOM/manifest/version.json");
        MainViewModel.OfficialManifestName.Should().Be("NOMNOM");
    }

    [Fact]
    public void ManifestSources_ShouldContainOnlyOfficialSource()
    {
        var db = new DatabaseService();
        var pathService = new PathService(db);
        var installService = new InstallService(pathService);
        var http = new HttpClient();
        var gh = new GitHubClient(new ProductHeaderValue("Yellowcake-Test"));

        var vm = new MainViewModel(
            db,
            installService,
            modService: null,
            manifestService: null,
            downloadQueue: new DownloadQueue(2),
            shutdownCts: new CancellationTokenSource(),
            http,
            gh,
            themeService: new ThemeService());

        vm.ManifestSources.Should().HaveCount(1);
        vm.ManifestSources.Keys.Single().Should().Be(MainViewModel.OfficialManifestName);
        vm.ManifestSources.Values.Single().Should().Be(MainViewModel.OfficialManifestUrl);
    }
}
