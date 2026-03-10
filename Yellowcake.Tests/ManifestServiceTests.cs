using System.Net;
using FluentAssertions;
using Yellowcake.Services;
using Yellowcake.Tests.Infrastructure;

namespace Yellowcake.Tests;

public class ManifestServiceTests : IDisposable
{
    private readonly string _tempLocalAppData;
    private readonly ScopedEnvironmentVariable _scopedLocalAppData;

    public ManifestServiceTests()
    {
        _tempLocalAppData = Path.Combine(Path.GetTempPath(), "YellowcakeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempLocalAppData);
        _scopedLocalAppData = new ScopedEnvironmentVariable("LOCALAPPDATA", _tempLocalAppData);
    }

    [Fact]
    public async Task FetchRemoteManifestAsync_ShouldReturnEmptyWhenTargetMissing()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not call HTTP"));
        using var http = new HttpClient(handler);
        var sut = new ManifestService(http);
        sut.ClearCache();

        var mods = await sut.FetchRemoteManifestAsync();

        mods.Should().BeEmpty();
        handler.SeenUris.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchRemoteManifestAsync_ShouldParseValidManifestAndSetSource()
    {
        var json = """
        [
          {
            "id": "mod.alpha",
            "displayName": "Alpha",
            "artifacts": [
              {
                "category": "release",
                "type": "Plugin",
                "version": "1.0.0",
                "downloadUrl": "https://example.test/alpha.zip"
              }
            ]
          }
        ]
        """;

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(json));
        using var http = new HttpClient(handler);
        var sut = new ManifestService(http) { TargetUrl = "https://example.test/manifest.json" };
        sut.ClearCache();

        var mods = await sut.FetchRemoteManifestAsync();

        mods.Should().HaveCount(1);
        mods[0].Id.Should().Be("mod.alpha");
        mods[0].Source.Should().Be("Remote");

        var cacheInfo = sut.GetCacheInfo();
        cacheInfo.Exists.Should().BeTrue();
        cacheInfo.Size.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FetchRemoteManifestAsync_ShouldFallbackToCacheOnHttpFailure()
    {
        var validJson = """
        [
          {
            "id": "mod.cached",
            "displayName": "Cached Mod",
            "artifacts": [
              {
                "category": "release",
                "type": "Plugin",
                "version": "2.0.0",
                "downloadUrl": "https://example.test/cached.zip"
              }
            ]
          }
        ]
        """;

        var firstHandler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(validJson));
        using (var firstHttp = new HttpClient(firstHandler))
        {
            var warmup = new ManifestService(firstHttp) { TargetUrl = "https://example.test/manifest.json" };
            warmup.ClearCache();
            var warmupResult = await warmup.FetchRemoteManifestAsync();
            warmupResult.Should().HaveCount(1);
        }

        var failingHandler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("server error", HttpStatusCode.InternalServerError));
        using var failingHttp = new HttpClient(failingHandler);
        var sut = new ManifestService(failingHttp) { TargetUrl = "https://example.test/manifest.json" };

        var result = await sut.FetchRemoteManifestAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("mod.cached");
    }

    [Fact]
    public async Task ClearCache_ShouldDeleteManifestCache()
    {
        var json = """
        [
          {
            "id": "mod.temp",
            "displayName": "Temp",
            "artifacts": [
              {
                "category": "release",
                "type": "Plugin",
                "version": "1.0.0",
                "downloadUrl": "https://example.test/temp.zip"
              }
            ]
          }
        ]
        """;

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(json));
        using var http = new HttpClient(handler);
        var sut = new ManifestService(http) { TargetUrl = "https://example.test/manifest.json" };
        sut.ClearCache();

        var mods = await sut.FetchRemoteManifestAsync();
        mods.Should().NotBeEmpty();
        sut.GetCacheInfo().Exists.Should().BeTrue();

        sut.ClearCache();

        sut.GetCacheInfo().Exists.Should().BeFalse();
    }

    [Fact]
    public async Task FetchRemoteManifestAsync_BackgroundRefresh_ShouldNotCallHttpWhenTokenCancelled()
    {
        // Warm the cache with a first successful fetch
        var warmJson = """
        [
          {
            "id": "mod.warm",
            "displayName": "Warm Mod",
            "artifacts": [
              {
                "category": "release",
                "type": "Plugin",
                "version": "1.0.0",
                "downloadUrl": "https://example.test/warm.zip"
              }
            ]
          }
        ]
        """;

        var warmHandler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(warmJson));
        using (var warmHttp = new HttpClient(warmHandler))
        {
            var warmup = new ManifestService(warmHttp) { TargetUrl = "https://example.test/manifest.json" };
            warmup.ClearCache();
            await warmup.FetchRemoteManifestAsync();
        }

        // Now use a handler that should NOT be invoked — cache is fresh + token is pre-cancelled
        var unexpectedHandler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(warmJson));
        using var http = new HttpClient(unexpectedHandler);
        var sut = new ManifestService(http) { TargetUrl = "https://example.test/manifest.json" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.FetchRemoteManifestAsync(cts.Token);

        // Should return cached mods
        result.Should().HaveCount(1);

        // Give any background tasks a moment to (not) run
        await Task.Delay(50);

        // The background Task.Run receives the pre-cancelled token and is never scheduled
        unexpectedHandler.SeenUris.Should().BeEmpty();
    }

    public void Dispose()
    {
        _scopedLocalAppData.Dispose();
        try
        {
            if (Directory.Exists(_tempLocalAppData))
            {
                Directory.Delete(_tempLocalAppData, recursive: true);
            }
        }
        catch
        {
        }
    }
}
